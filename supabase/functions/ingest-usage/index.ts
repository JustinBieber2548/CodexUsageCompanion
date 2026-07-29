import {
    assertExactKeys,
    authenticateUser,
    handleError,
    isRecord,
    JsonRecord,
    jsonResponse,
    readJsonObject,
    RequestError,
    userRestFetch,
} from "../_shared/http.ts";

const payloadKeys = [
    "deviceId",
    "deviceName",
    "appVersion",
    "sourceType",
    "sourceFreshness",
    "capturedAtUtc",
    "shortTerm",
    "weekly",
    "errorCode",
    "sanitizedErrorMessage",
] as const;

const windowKeys = [
    "name",
    "remainingPercent",
    "usedPercent",
    "resetAtUtc",
    "windowSeconds",
    "confirmed",
    "estimated",
] as const;

const allowedFreshness = new Set([
    "fresh",
    "stale",
    "offline",
    "authenticationrequired",
    "unsupportedsource",
    "readererror",
]);

interface UsageWindowInput {
    name: string | null;
    remainingPercent: number | null;
    usedPercent: number | null;
    resetAtUtc: string | null;
    windowSeconds: number | null;
    confirmed: boolean;
    estimated: boolean;
}

export interface IngestPayload {
    deviceId: string;
    deviceName: string;
    appVersion: string;
    sourceType: string;
    sourceFreshness: string;
    capturedAtUtc: string;
    shortTerm: UsageWindowInput | null;
    weekly: UsageWindowInput | null;
    errorCode: string | null;
    sanitizedErrorMessage: string | null;
}

export function validatePayload(value: JsonRecord): IngestPayload {
    assertExactKeys(value, payloadKeys);
    const deviceId = requiredString(value.deviceId, 36);
    if (
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
            .test(
                deviceId,
            )
    ) {
        throw new RequestError(400, "invalid_device_id");
    }

    const deviceName = requiredString(value.deviceName, 100);
    const appVersion = requiredString(value.appVersion, 100);
    if (
        !/^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/
            .test(
                appVersion,
            )
    ) {
        throw new RequestError(400, "invalid_app_version");
    }

    const sourceType = requiredString(value.sourceType, 100);
    const sourceFreshness = requiredString(value.sourceFreshness, 50);
    if (!allowedFreshness.has(sourceFreshness)) {
        throw new RequestError(400, "invalid_freshness");
    }

    const capturedAtUtc = requiredDate(value.capturedAtUtc);
    if (new Date(capturedAtUtc).getTime() > Date.now() + 5 * 60 * 1000) {
        throw new RequestError(400, "invalid_captured_at");
    }

    return {
        deviceId,
        deviceName,
        appVersion,
        sourceType,
        sourceFreshness,
        capturedAtUtc,
        shortTerm: validateWindow(value.shortTerm),
        weekly: validateWindow(value.weekly),
        errorCode: optionalString(value.errorCode, 100),
        sanitizedErrorMessage: optionalString(value.sanitizedErrorMessage, 500),
    };
}

export async function handleRequest(request: Request): Promise<Response> {
    try {
        if (request.method !== "POST") {
            return jsonResponse({ error: "method_not_allowed" }, 405, {
                allow: "POST",
            });
        }

        await authenticateUser(request);
        const payload = validatePayload(
            await readJsonObject(request, 16 * 1024),
        );
        const shortTerm = payload.shortTerm;
        const weekly = payload.weekly;
        const rpcResponse = await userRestFetch(
            request,
            "/rest/v1/rpc/ingest_usage",
            {
                method: "POST",
                body: JSON.stringify({
                    p_device_id: payload.deviceId,
                    p_device_name: payload.deviceName,
                    p_app_version: payload.appVersion,
                    p_source_type: payload.sourceType,
                    p_source_freshness: payload.sourceFreshness,
                    p_captured_at: payload.capturedAtUtc,
                    p_short_term_name: shortTerm?.name ?? null,
                    p_short_term_remaining: shortTerm?.remainingPercent ?? null,
                    p_short_term_used: shortTerm?.usedPercent ?? null,
                    p_short_term_reset_at: shortTerm?.resetAtUtc ?? null,
                    p_short_term_window_seconds: shortTerm?.windowSeconds ??
                        null,
                    p_short_term_confirmed: shortTerm?.confirmed ?? false,
                    p_short_term_estimated: shortTerm?.estimated ?? false,
                    p_weekly_name: weekly?.name ?? null,
                    p_weekly_remaining: weekly?.remainingPercent ?? null,
                    p_weekly_used: weekly?.usedPercent ?? null,
                    p_weekly_reset_at: weekly?.resetAtUtc ?? null,
                    p_weekly_window_seconds: weekly?.windowSeconds ?? null,
                    p_weekly_confirmed: weekly?.confirmed ?? false,
                    p_weekly_estimated: weekly?.estimated ?? false,
                    p_error_code: payload.errorCode,
                    p_error_message_sanitized: payload.sanitizedErrorMessage,
                }),
            },
        );

        if (!rpcResponse.ok) {
            const errorText = await rpcResponse.text();
            if (errorText.includes("rate_limited")) {
                return jsonResponse({ error: "rate_limited" }, 429);
            }

            return jsonResponse({ error: "ingest_rejected" }, 400);
        }

        return jsonResponse({ ok: true, result: await rpcResponse.json() });
    } catch (error) {
        return handleError(error);
    }
}

function validateWindow(value: unknown): UsageWindowInput | null {
    if (value === null || value === undefined) {
        return null;
    }

    if (!isRecord(value)) {
        throw new RequestError(400, "invalid_window");
    }

    assertExactKeys(value, windowKeys);
    const confirmed = requiredBoolean(value.confirmed);
    const estimated = requiredBoolean(value.estimated);
    if (confirmed && estimated) {
        throw new RequestError(400, "invalid_window_confidence");
    }

    return {
        name: optionalString(value.name, 100),
        remainingPercent: optionalPercent(value.remainingPercent),
        usedPercent: optionalPercent(value.usedPercent),
        resetAtUtc: optionalDate(value.resetAtUtc),
        windowSeconds: optionalPositiveInteger(value.windowSeconds),
        confirmed,
        estimated,
    };
}

function requiredString(value: unknown, maximumLength: number): string {
    if (
        typeof value !== "string" || value.trim().length === 0 ||
        value.length > maximumLength
    ) {
        throw new RequestError(400, "invalid_string");
    }

    return value;
}

function optionalString(value: unknown, maximumLength: number): string | null {
    if (value === null || value === undefined) {
        return null;
    }

    return requiredString(value, maximumLength);
}

function requiredBoolean(value: unknown): boolean {
    if (typeof value !== "boolean") {
        throw new RequestError(400, "invalid_boolean");
    }

    return value;
}

function optionalPercent(value: unknown): number | null {
    if (value === null || value === undefined) {
        return null;
    }

    if (
        typeof value !== "number" || !Number.isFinite(value) || value < 0 ||
        value > 100
    ) {
        throw new RequestError(400, "invalid_percentage");
    }

    return value;
}

function optionalPositiveInteger(value: unknown): number | null {
    if (value === null || value === undefined) {
        return null;
    }

    if (!Number.isSafeInteger(value) || (value as number) <= 0) {
        throw new RequestError(400, "invalid_window_seconds");
    }

    return value as number;
}

function requiredDate(value: unknown): string {
    if (typeof value !== "string") {
        throw new RequestError(400, "invalid_timestamp");
    }

    const parsed = new Date(value);
    if (
        Number.isNaN(parsed.getTime()) ||
        parsed.getTime() < Date.UTC(2020, 0, 1)
    ) {
        throw new RequestError(400, "invalid_timestamp");
    }

    return parsed.toISOString();
}

function optionalDate(value: unknown): string | null {
    if (value === null || value === undefined) {
        return null;
    }

    return requiredDate(value);
}

if (import.meta.main) {
    Deno.serve(handleRequest);
}
