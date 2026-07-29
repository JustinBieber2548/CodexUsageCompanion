export type JsonRecord = Record<string, unknown>;

export function jsonResponse(
    body: unknown,
    status = 200,
    extraHeaders: HeadersInit = {},
): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: {
            "content-type": "application/json; charset=utf-8",
            "cache-control": "no-store",
            ...extraHeaders,
        },
    });
}

export async function readJsonObject(
    request: Request,
    maximumBytes: number,
): Promise<JsonRecord> {
    const declaredLength = Number(request.headers.get("content-length") ?? "0");
    if (Number.isFinite(declaredLength) && declaredLength > maximumBytes) {
        throw new RequestError(413, "payload_too_large");
    }

    const text = await request.text();
    if (new TextEncoder().encode(text).byteLength > maximumBytes) {
        throw new RequestError(413, "payload_too_large");
    }

    let value: unknown;
    try {
        value = JSON.parse(text);
    } catch {
        throw new RequestError(400, "invalid_json");
    }

    if (!isRecord(value)) {
        throw new RequestError(400, "invalid_payload");
    }

    return value;
}

export function assertExactKeys(
    value: JsonRecord,
    allowedKeys: readonly string[],
): void {
    const allowed = new Set(allowedKeys);
    if (Object.keys(value).some((key) => !allowed.has(key))) {
        throw new RequestError(400, "unknown_field");
    }
}

export async function authenticateUser(request: Request): Promise<string> {
    const authorization = request.headers.get("authorization") ?? "";
    if (!authorization.startsWith("Bearer ")) {
        throw new RequestError(401, "authentication_required");
    }

    const response = await fetch(`${supabaseUrl()}/auth/v1/user`, {
        headers: {
            authorization,
            apikey: publishableKey(),
        },
    });
    if (!response.ok) {
        throw new RequestError(401, "authentication_required");
    }

    const user = await response.json() as JsonRecord;
    if (typeof user.id !== "string" || user.id.length === 0) {
        throw new RequestError(401, "authentication_required");
    }

    return user.id;
}

export async function userRestFetch(
    request: Request,
    path: string,
    init: RequestInit,
): Promise<Response> {
    const authorization = request.headers.get("authorization");
    if (!authorization) {
        throw new RequestError(401, "authentication_required");
    }

    return await fetch(`${supabaseUrl()}${path}`, {
        ...init,
        headers: {
            "content-type": "application/json",
            apikey: publishableKey(),
            authorization,
            ...(init.headers ?? {}),
        },
    });
}

export async function adminRestFetch(
    path: string,
    init: RequestInit = {},
): Promise<Response> {
    return await fetch(`${supabaseUrl()}${path}`, {
        ...init,
        headers: {
            "content-type": "application/json",
            apikey: secretKey(),
            ...(init.headers ?? {}),
        },
    });
}

export function requireSharedSecret(
    request: Request,
    headerName: string,
    environmentName: string,
): void {
    const expected = Deno.env.get(environmentName);
    const actual = request.headers.get(headerName);
    if (!expected || !actual || !constantTimeEqual(expected, actual)) {
        throw new RequestError(401, "authentication_required");
    }
}

export class RequestError extends Error {
    constructor(
        public readonly status: number,
        public readonly code: string,
    ) {
        super(code);
    }
}

export function handleError(error: unknown): Response {
    if (error instanceof RequestError) {
        return jsonResponse({ error: error.code }, error.status);
    }

    console.error("sanitized_function_error");
    return jsonResponse({ error: "internal_error" }, 500);
}

export function isRecord(value: unknown): value is JsonRecord {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function supabaseUrl(): string {
    const value = Deno.env.get("SUPABASE_URL");
    if (!value) {
        throw new Error("SUPABASE_URL is unavailable");
    }

    return value.replace(/\/+$/, "");
}

export function publishableKey(): string {
    return namedKey("SUPABASE_PUBLISHABLE_KEYS", "SUPABASE_ANON_KEY");
}

export function secretKey(): string {
    return namedKey("SUPABASE_SECRET_KEYS", "SUPABASE_SERVICE_ROLE_KEY");
}

function namedKey(
    jsonEnvironmentName: string,
    legacyEnvironmentName: string,
): string {
    const raw = Deno.env.get(jsonEnvironmentName);
    if (raw) {
        try {
            const values = JSON.parse(raw) as Record<string, unknown>;
            if (
                typeof values.default === "string" && values.default.length > 0
            ) {
                return values.default;
            }
        } catch {
            throw new Error(`${jsonEnvironmentName} is invalid`);
        }
    }

    const legacy = Deno.env.get(legacyEnvironmentName);
    if (!legacy) {
        throw new Error(`${jsonEnvironmentName} is unavailable`);
    }

    return legacy;
}

function constantTimeEqual(left: string, right: string): boolean {
    const leftBytes = new TextEncoder().encode(left);
    const rightBytes = new TextEncoder().encode(right);
    const length = Math.max(leftBytes.length, rightBytes.length);
    let difference = leftBytes.length ^ rightBytes.length;
    for (let index = 0; index < length; index++) {
        difference |= (leftBytes[index] ?? 0) ^ (rightBytes[index] ?? 0);
    }

    return difference === 0;
}
