import { formatUsageMessage } from "../_shared/telegram.ts";

Deno.test("usage command labels fresh data as online", () => {
    const now = new Date("2026-07-29T09:03:34Z");
    const message = formatUsageMessage(
        {
            short_term_remaining: 62,
            short_term_reset_at: "2026-07-29T11:30:00Z",
            weekly_remaining: 83,
            weekly_reset_at: "2026-08-01T17:00:00Z",
            captured_at: "2026-07-29T09:03:00Z",
            source_freshness: "fresh",
        },
        now,
    );
    assertIncludes(message, "Device: Online");
    assertIncludes(message, "62% remaining");
});

Deno.test("usage command clearly labels stale data", () => {
    const now = new Date("2026-07-29T11:03:00Z");
    const message = formatUsageMessage(
        {
            short_term_remaining: 62,
            weekly_remaining: 83,
            captured_at: "2026-07-29T09:03:00Z",
            source_freshness: "fresh",
        },
        now,
    );
    assertIncludes(message, "last known reading");
    assertIncludes(message, "Offline or stale");
});

function assertIncludes(value: string, expected: string): void {
    if (!value.includes(expected)) {
        throw new Error(`Expected '${value}' to include '${expected}'`);
    }
}
