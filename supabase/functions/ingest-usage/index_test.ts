import { RequestError } from "../_shared/http.ts";
import { validatePayload } from "./index.ts";

Deno.test("ingest accepts a normalized snapshot", () => {
    const payload = validatePayload(validPayload());
    if (payload.shortTerm?.remainingPercent !== 62) {
        throw new Error("short-term percentage was not preserved");
    }
});

Deno.test("ingest rejects an invalid percentage", () => {
    const input = validPayload();
    (input.shortTerm as Record<string, unknown>).remainingPercent = 101;
    assertRequestError(() => validatePayload(input), "invalid_percentage");
});

Deno.test("ingest rejects unknown fields", () => {
    const input = validPayload();
    input.authorization = "must never be accepted";
    assertRequestError(() => validatePayload(input), "unknown_field");
});

Deno.test("ingest rejects invalid application versions", () => {
    const input = validPayload();
    input.appVersion = "release-one";
    assertRequestError(() => validatePayload(input), "invalid_app_version");
});

function validPayload(): Record<string, unknown> {
    return {
        deviceId: "8c656659-a8c0-4b95-b706-89f079f3208b",
        deviceName: "Test device",
        appVersion: "1.0.0",
        sourceType: "mock",
        sourceFreshness: "fresh",
        capturedAtUtc: "2026-07-29T09:03:00Z",
        shortTerm: {
            name: "5-hour window",
            remainingPercent: 62,
            usedPercent: 38,
            resetAtUtc: "2026-07-29T11:30:00Z",
            windowSeconds: 18000,
            confirmed: true,
            estimated: false,
        },
        weekly: {
            name: "Weekly window",
            remainingPercent: 83,
            usedPercent: 17,
            resetAtUtc: "2026-08-01T17:00:00Z",
            windowSeconds: 604800,
            confirmed: true,
            estimated: false,
        },
        errorCode: null,
        sanitizedErrorMessage: null,
    };
}

function assertRequestError(action: () => unknown, expectedCode: string): void {
    try {
        action();
    } catch (error) {
        if (error instanceof RequestError && error.code === expectedCode) {
            return;
        }

        throw error;
    }

    throw new Error(`Expected RequestError ${expectedCode}`);
}
