import {
    adminRestFetch,
    handleError,
    JsonRecord,
    jsonResponse,
    requireSharedSecret,
} from "../_shared/http.ts";
import { sendTelegramMessage, UsageRow } from "../_shared/telegram.ts";

interface ProfileRow extends JsonRecord {
    id: string;
    telegram_chat_id: string;
}

interface PreferenceRow extends JsonRecord {
    telegram_enabled?: boolean;
    short_term_thresholds?: number[];
    weekly_thresholds?: number[];
    short_term_reset_warning?: boolean;
    reset_notifications?: boolean;
    offline_timeout_minutes?: number;
}

interface DeviceRow extends JsonRecord {
    id: string;
    device_name: string;
    last_heartbeat_at?: string | null;
    last_successful_read_at?: string | null;
    last_error_code?: string | null;
}

interface CloudEvent {
    idempotencyKey: string;
    eventKey: string;
    message: string;
    threshold?: number;
    deviceId?: string;
}

export async function handleRequest(request: Request): Promise<Response> {
    try {
        if (request.method !== "POST") {
            return jsonResponse({ error: "method_not_allowed" }, 405, {
                allow: "POST",
            });
        }

        requireSharedSecret(
            request,
            "x-cron-secret",
            "NOTIFICATION_CHECK_CRON_SECRET",
        );
        const profiles = await fetchProfiles();
        let delivered = 0;
        for (const profile of profiles) {
            const preferences = await fetchPreferences(profile.id);
            if (preferences.telegram_enabled === false) {
                continue;
            }

            const snapshots = await fetchRecentUsage(profile.id);
            const events = buildUsageEvents(
                profile.id,
                snapshots[0] ?? null,
                snapshots[1] ?? null,
                preferences,
                new Date(),
            );
            const devices = await fetchDevices(profile.id);
            events.push(
                ...buildDeviceEvents(
                    profile.id,
                    devices,
                    preferences,
                    new Date(),
                ),
            );

            for (const event of events) {
                if (await insertAndDeliver(profile, event)) {
                    delivered++;
                }
            }

            delivered += await deliverRestoredEvents(profile, devices);
        }

        return jsonResponse({ ok: true, delivered });
    } catch (error) {
        return handleError(error);
    }
}

export function buildUsageEvents(
    profileId: string,
    latest: UsageRow | null,
    previous: UsageRow | null,
    preferences: PreferenceRow,
    now: Date,
): CloudEvent[] {
    if (!latest) {
        return [];
    }

    const events: CloudEvent[] = [];
    appendWindowEvents(
        events,
        profileId,
        "short-term",
        numberOrNull(latest.short_term_remaining),
        dateOrNull(latest.short_term_reset_at),
        Array.isArray(preferences.short_term_thresholds)
            ? preferences.short_term_thresholds
            : [10, 5, 0],
        preferences.short_term_reset_warning !== false,
        now,
    );
    appendWindowEvents(
        events,
        profileId,
        "weekly",
        numberOrNull(latest.weekly_remaining),
        dateOrNull(latest.weekly_reset_at),
        Array.isArray(preferences.weekly_thresholds)
            ? preferences.weekly_thresholds
            : [10, 5],
        false,
        now,
    );

    if (preferences.reset_notifications !== false && previous) {
        appendConfirmedReset(
            events,
            profileId,
            "short-term",
            numberOrNull(latest.short_term_remaining),
            dateOrNull(latest.short_term_reset_at),
            numberOrNull(previous.short_term_remaining),
            dateOrNull(previous.short_term_reset_at),
        );
        appendConfirmedReset(
            events,
            profileId,
            "weekly",
            numberOrNull(latest.weekly_remaining),
            dateOrNull(latest.weekly_reset_at),
            numberOrNull(previous.weekly_remaining),
            dateOrNull(previous.weekly_reset_at),
        );
    }

    return events;
}

export function buildDeviceEvents(
    profileId: string,
    devices: DeviceRow[],
    preferences: PreferenceRow,
    now: Date,
): CloudEvent[] {
    const events: CloudEvent[] = [];
    const offlineMinutes = Math.max(
        1,
        Math.min(1440, Number(preferences.offline_timeout_minutes ?? 15)),
    );
    for (const device of devices) {
        const heartbeat = dateOrNull(device.last_heartbeat_at);
        const lastSuccess = dateOrNull(device.last_successful_read_at);
        if (
            !heartbeat ||
            now.getTime() - heartbeat.getTime() >= offlineMinutes * 60 * 1000
        ) {
            const epoch = lastSuccess?.toISOString() ?? "never";
            events.push({
                idempotencyKey: `offline:${device.id}:${epoch}`,
                eventKey: "monitoring_offline",
                message:
                    `⚠️ Codex monitoring is offline on ${device.device_name}. No successful reading for at least ${offlineMinutes} minutes.`,
                deviceId: device.id,
            });
        }

        if (
            typeof device.last_error_code === "string" &&
            device.last_error_code.includes("auth")
        ) {
            events.push({
                idempotencyKey:
                    `authentication:${device.id}:${device.last_error_code}`,
                eventKey: "authentication_required",
                message:
                    `⚠️ Codex authentication needs attention on ${device.device_name}. Open Codex, sign in, and refresh the companion.`,
                deviceId: device.id,
            });
        }
    }

    return events.map((event) => ({
        ...event,
        idempotencyKey: `${profileId}:${event.idempotencyKey}`,
    }));
}

async function insertAndDeliver(
    profile: ProfileRow,
    event: CloudEvent,
): Promise<boolean> {
    const insertResponse = await adminRestFetch(
        "/rest/v1/notification_events",
        {
            method: "POST",
            headers: {
                prefer: "resolution=ignore-duplicates,return=representation",
            },
            body: JSON.stringify({
                profile_id: profile.id,
                device_id: event.deviceId ?? null,
                idempotency_key: event.idempotencyKey,
                event_key: event.eventKey,
                channel: "telegram",
                threshold: event.threshold ?? null,
                status: "pending",
            }),
        },
    );
    if (!insertResponse.ok) {
        throw new Error("notification_event_insert_failed");
    }

    const inserted = await insertResponse.json() as JsonRecord[];
    let delivery = inserted[0];
    if (!delivery) {
        const query = new URLSearchParams({
            profile_id: `eq.${profile.id}`,
            idempotency_key: `eq.${event.idempotencyKey}`,
            status: "eq.failed",
            select: "id,attempt_count,next_attempt_at",
            limit: "1",
        });
        const retryResponse = await adminRestFetch(
            `/rest/v1/notification_events?${query}`,
        );
        if (!retryResponse.ok) {
            throw new Error("notification_retry_query_failed");
        }

        const retryRows = await retryResponse.json() as JsonRecord[];
        delivery = retryRows[0];
        const attemptCount = Number(delivery?.attempt_count ?? 0);
        const nextAttemptAt = typeof delivery?.next_attempt_at === "string"
            ? new Date(delivery.next_attempt_at)
            : null;
        if (
            !delivery || attemptCount >= 6 ||
            (nextAttemptAt && nextAttemptAt > new Date())
        ) {
            return false;
        }
    }

    const eventId = Number(delivery.id);
    const attemptCount = Number(delivery.attempt_count ?? 0) + 1;
    const sent = await sendTelegramMessage(
        profile.telegram_chat_id,
        event.message,
    );
    await adminRestFetch(`/rest/v1/notification_events?id=eq.${eventId}`, {
        method: "PATCH",
        body: JSON.stringify(
            sent
                ? {
                    status: "delivered",
                    delivered_at: new Date().toISOString(),
                    error_sanitized: null,
                    attempt_count: attemptCount,
                    next_attempt_at: null,
                }
                : {
                    status: "failed",
                    error_sanitized: "telegram_api_failure",
                    attempt_count: attemptCount,
                    next_attempt_at: new Date(
                        Date.now() +
                            Math.min(30 * 2 ** attemptCount, 1800) * 1000,
                    ).toISOString(),
                },
        ),
    });
    return sent;
}

async function deliverRestoredEvents(
    profile: ProfileRow,
    devices: DeviceRow[],
): Promise<number> {
    let delivered = 0;
    for (const device of devices) {
        const heartbeat = dateOrNull(device.last_heartbeat_at);
        if (!heartbeat || Date.now() - heartbeat.getTime() >= 15 * 60 * 1000) {
            continue;
        }

        const query = new URLSearchParams({
            profile_id: `eq.${profile.id}`,
            device_id: `eq.${device.id}`,
            event_key: "eq.monitoring_offline",
            status: "eq.delivered",
            select: "id,created_at",
            order: "created_at.desc",
            limit: "1",
        });
        const response = await adminRestFetch(
            `/rest/v1/notification_events?${query}`,
        );
        if (!response.ok) {
            throw new Error("offline_event_query_failed");
        }

        const rows = await response.json() as JsonRecord[];
        const offline = rows[0];
        const offlineAt = dateOrNull(offline?.created_at);
        if (!offline || !offlineAt || heartbeat <= offlineAt) {
            continue;
        }

        const event: CloudEvent = {
            idempotencyKey: `${profile.id}:restored:${String(offline.id)}`,
            eventKey: "monitoring_restored",
            message:
                `✅ Codex monitoring is restored on ${device.device_name}.`,
            deviceId: device.id,
        };
        if (await insertAndDeliver(profile, event)) {
            delivered++;
        }
    }

    return delivered;
}

function appendWindowEvents(
    events: CloudEvent[],
    profileId: string,
    label: string,
    remaining: number | null,
    resetAt: Date | null,
    thresholds: number[],
    resetWarning: boolean,
    now: Date,
): void {
    const resetKey = resetAt?.toISOString() ?? "unknown";
    if (remaining !== null) {
        for (const threshold of thresholds) {
            if (
                Number.isFinite(threshold) && threshold >= 0 &&
                threshold <= 100 &&
                remaining <= threshold
            ) {
                events.push({
                    idempotencyKey:
                        `${profileId}:threshold:${label}:${resetKey}:${threshold}`,
                    eventKey: `${label}_threshold`,
                    message: `⚠️ Codex ${label}: ${
                        round(remaining)
                    }% remaining. The ${threshold}% threshold was reached.`,
                    threshold,
                });
            }
        }
    }

    if (!resetAt) {
        return;
    }

    const milliseconds = resetAt.getTime() - now.getTime();
    if (resetWarning && milliseconds > 0 && milliseconds <= 10 * 60 * 1000) {
        events.push({
            idempotencyKey: `${profileId}:reset-warning:${label}:${resetKey}`,
            eventKey: `${label}_reset_warning`,
            message: `⏳ Codex ${label} known reset time is in ${
                Math.max(1, Math.ceil(milliseconds / 60000))
            } minute(s).`,
        });
    } else if (milliseconds <= 0) {
        events.push({
            idempotencyKey: `${profileId}:reset-arrived:${label}:${resetKey}`,
            eventKey: `${label}_reset_arrived`,
            message:
                `⏳ Codex ${label} known reset time has arrived. Waiting for the next device reading to confirm the refreshed allowance.`,
        });
    }
}

function appendConfirmedReset(
    events: CloudEvent[],
    profileId: string,
    label: string,
    remaining: number | null,
    resetAt: Date | null,
    previousRemaining: number | null,
    previousResetAt: Date | null,
): void {
    if (
        remaining === null || previousRemaining === null || !resetAt ||
        !previousResetAt ||
        resetAt <= previousResetAt || remaining <= previousRemaining
    ) {
        return;
    }

    events.push({
        idempotencyKey:
            `${profileId}:reset-confirmed:${label}:${resetAt.toISOString()}`,
        eventKey: `${label}_reset_confirmed`,
        message: `✅ Codex ${label} allowance reset confirmed at ${
            round(remaining)
        }% remaining.`,
    });
}

async function fetchProfiles(): Promise<ProfileRow[]> {
    const response = await adminRestFetch(
        "/rest/v1/profiles?telegram_chat_id=not.is.null&select=id,telegram_chat_id",
    );
    if (!response.ok) {
        throw new Error("profiles_query_failed");
    }

    return await response.json() as ProfileRow[];
}

async function fetchPreferences(profileId: string): Promise<PreferenceRow> {
    const query = new URLSearchParams({
        profile_id: `eq.${profileId}`,
        select:
            "telegram_enabled,short_term_thresholds,weekly_thresholds,short_term_reset_warning,reset_notifications,offline_timeout_minutes",
        limit: "1",
    });
    const response = await adminRestFetch(
        `/rest/v1/notification_preferences?${query}`,
    );
    if (!response.ok) {
        throw new Error("preferences_query_failed");
    }

    const rows = await response.json() as PreferenceRow[];
    return rows[0] ?? {};
}

async function fetchRecentUsage(profileId: string): Promise<UsageRow[]> {
    const query = new URLSearchParams({
        profile_id: `eq.${profileId}`,
        select:
            "short_term_remaining,short_term_reset_at,weekly_remaining,weekly_reset_at,captured_at,received_at,source_freshness",
        order: "captured_at.desc",
        limit: "2",
    });
    const response = await adminRestFetch(`/rest/v1/usage_snapshots?${query}`);
    if (!response.ok) {
        throw new Error("usage_snapshots_query_failed");
    }

    return await response.json() as UsageRow[];
}

async function fetchDevices(profileId: string): Promise<DeviceRow[]> {
    const query = new URLSearchParams({
        profile_id: `eq.${profileId}`,
        select:
            "id,device_name,last_heartbeat_at,last_successful_read_at,last_error_code",
    });
    const response = await adminRestFetch(`/rest/v1/devices?${query}`);
    if (!response.ok) {
        throw new Error("devices_query_failed");
    }

    return await response.json() as DeviceRow[];
}

function numberOrNull(value: unknown): number | null {
    return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function dateOrNull(value: unknown): Date | null {
    if (typeof value !== "string") {
        return null;
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
}

function round(value: number): number {
    return Math.round(value * 10) / 10;
}

if (import.meta.main) {
    Deno.serve(handleRequest);
}
