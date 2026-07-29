import {
    adminRestFetch,
    handleError,
    JsonRecord,
    jsonResponse,
    readJsonObject,
    RequestError,
    requireSharedSecret,
} from "../_shared/http.ts";
import {
    fetchLatestUsage,
    formatUsageMessage,
    sendTelegramMessage,
} from "../_shared/telegram.ts";

interface TelegramMessage {
    chat?: { id?: number | string };
    text?: string;
}

interface TelegramUpdate extends JsonRecord {
    update_id?: number;
    message?: TelegramMessage;
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
            "x-telegram-bot-api-secret-token",
            "TELEGRAM_WEBHOOK_SECRET",
        );
        const update = await readJsonObject(
            request,
            64 * 1024,
        ) as TelegramUpdate;
        if (!Number.isSafeInteger(update.update_id)) {
            throw new RequestError(400, "invalid_update");
        }

        const profileId = requiredEnvironment("AUTHORIZED_PROFILE_ID");
        if (!(await rememberUpdate(update.update_id!, profileId))) {
            return jsonResponse({ ok: true, duplicate: true });
        }

        const chatId = update.message?.chat?.id?.toString();
        const authorizedChatId = requiredEnvironment(
            "AUTHORIZED_TELEGRAM_CHAT_ID",
        );
        if (!chatId || chatId !== authorizedChatId) {
            return jsonResponse({ ok: true, ignored: true });
        }

        const command = normalizeCommand(update.message?.text);
        const responseText = await commandResponse(command, profileId);
        if (!(await sendTelegramMessage(chatId, responseText))) {
            throw new Error("telegram_send_failed");
        }

        return jsonResponse({ ok: true });
    } catch (error) {
        return handleError(error);
    }
}

export async function commandResponse(
    command: string,
    profileId: string,
): Promise<string> {
    switch (command) {
        case "/start":
            return [
                "🤖 Codex Usage Companion",
                "",
                "This bot reports the latest usage synchronized by your Windows device.",
                "It never sends prompts to Codex and clearly labels stale data.",
                "",
                "Use /usage, /status, /notifications, or /help.",
            ].join("\n");
        case "/usage":
            return formatUsageMessage(await fetchLatestUsage(profileId));
        case "/status":
            return await formatStatus(profileId);
        case "/notifications":
            return await formatNotifications(profileId);
        case "/help":
            return [
                "Codex Usage Companion commands",
                "",
                "/usage — latest known usage",
                "/status — device freshness and monitoring state",
                "/notifications — enabled alert types",
                "/help — this command list",
            ].join("\n");
        default:
            return "Unknown command. Use /help to list supported commands.";
    }
}

async function rememberUpdate(
    updateId: number,
    profileId: string,
): Promise<boolean> {
    const response = await adminRestFetch(
        "/rest/v1/processed_telegram_updates",
        {
            method: "POST",
            headers: {
                prefer: "resolution=ignore-duplicates,return=representation",
            },
            body: JSON.stringify({
                update_id: updateId,
                profile_id: profileId,
            }),
        },
    );
    if (!response.ok) {
        throw new Error("telegram_update_dedup_failed");
    }

    const rows = await response.json() as JsonRecord[];
    return rows.length > 0;
}

async function formatStatus(profileId: string): Promise<string> {
    const query = new URLSearchParams({
        profile_id: `eq.${profileId}`,
        select:
            "device_name,app_version,last_heartbeat_at,last_successful_read_at,last_error_code",
        order: "last_heartbeat_at.desc",
        limit: "1",
    });
    const response = await adminRestFetch(`/rest/v1/devices?${query}`);
    if (!response.ok) {
        throw new Error("device_status_query_failed");
    }

    const rows = await response.json() as JsonRecord[];
    const device = rows[0];
    if (!device) {
        return "Codex monitoring status\n\nNo device has synchronized yet.";
    }

    const heartbeat = typeof device.last_heartbeat_at === "string"
        ? new Date(device.last_heartbeat_at)
        : null;
    const online = heartbeat !== null &&
        !Number.isNaN(heartbeat.getTime()) &&
        Date.now() - heartbeat.getTime() < 15 * 60 * 1000;
    return [
        "Codex monitoring status",
        "",
        `Device: ${String(device.device_name ?? "Unknown")}`,
        `State: ${online ? "Online" : "Offline or stale"}`,
        `App version: ${String(device.app_version ?? "Unknown")}`,
        `Last error: ${String(device.last_error_code ?? "None")}`,
    ].join("\n");
}

async function formatNotifications(profileId: string): Promise<string> {
    const query = new URLSearchParams({
        profile_id: `eq.${profileId}`,
        select:
            "telegram_enabled,short_term_thresholds,weekly_thresholds,short_term_reset_warning,reset_notifications,offline_timeout_minutes,usage_change_enabled",
        limit: "1",
    });
    const response = await adminRestFetch(
        `/rest/v1/notification_preferences?${query}`,
    );
    if (!response.ok) {
        throw new Error("notification_preferences_query_failed");
    }

    const rows = await response.json() as JsonRecord[];
    const preferences = rows[0] ?? {};
    return [
        "Codex notification settings",
        "",
        `Telegram: ${
            preferences.telegram_enabled === false ? "Disabled" : "Enabled"
        }`,
        `Short-term thresholds: ${
            formatArray(preferences.short_term_thresholds, [10, 5, 0])
        }`,
        `Weekly thresholds: ${
            formatArray(preferences.weekly_thresholds, [10, 5])
        }`,
        `Reset alerts: ${
            preferences.reset_notifications === false ? "Disabled" : "Enabled"
        }`,
        `Reset warning: ${
            preferences.short_term_reset_warning === false
                ? "Disabled"
                : "Enabled"
        }`,
        `Offline timeout: ${
            Number(preferences.offline_timeout_minutes ?? 15)
        } minutes`,
        `Every usage change: ${
            preferences.usage_change_enabled === true ? "Enabled" : "Disabled"
        }`,
    ].join("\n");
}

function normalizeCommand(text: unknown): string {
    if (typeof text !== "string") {
        return "";
    }

    return text.trim().split(/\s+/)[0].split("@")[0].toLowerCase();
}

function formatArray(value: unknown, fallback: number[]): string {
    const values = Array.isArray(value) ? value : fallback;
    return values.map((item) => `${Number(item)}%`).join(", ");
}

function requiredEnvironment(name: string): string {
    const value = Deno.env.get(name);
    if (!value) {
        throw new Error(`${name} is unavailable`);
    }

    return value;
}

if (import.meta.main) {
    Deno.serve(handleRequest);
}
