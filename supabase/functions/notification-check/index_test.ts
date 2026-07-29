import { buildDeviceEvents, buildUsageEvents } from "./index.ts";

Deno.test("threshold event uses a stable reset-scoped idempotency key", () => {
    const events = buildUsageEvents(
        "9dfae945-726e-475c-9de1-e7f4cb3182ca",
        {
            short_term_remaining: 9,
            short_term_reset_at: "2026-07-29T11:30:00Z",
            weekly_remaining: 80,
            weekly_reset_at: "2026-08-01T17:00:00Z",
            captured_at: "2026-07-29T09:03:00Z",
            source_freshness: "fresh",
        },
        null,
        {},
        new Date("2026-07-29T09:03:00Z"),
    );
    const threshold = events.find((event) => event.threshold === 10);
    if (!threshold?.idempotencyKey.includes("2026-07-29T11:30:00.000Z")) {
        throw new Error("threshold event is not reset-scoped");
    }
});

Deno.test("passing reset time does not claim one hundred percent", () => {
    const events = buildUsageEvents(
        "9dfae945-726e-475c-9de1-e7f4cb3182ca",
        {
            short_term_remaining: 4,
            short_term_reset_at: "2026-07-29T09:00:00Z",
            captured_at: "2026-07-29T09:03:00Z",
            source_freshness: "fresh",
        },
        null,
        {},
        new Date("2026-07-29T09:03:00Z"),
    );
    const resetMessage = events.find((event) =>
        event.eventKey.includes("reset_arrived")
    )?.message;
    if (!resetMessage?.includes("Waiting for the next device reading")) {
        throw new Error(
            "reset arrival was not described as awaiting confirmation",
        );
    }

    if (resetMessage.includes("100%")) {
        throw new Error("reset arrival fabricated a percentage");
    }
});

Deno.test("offline device produces one stable event key", () => {
    const events = buildDeviceEvents(
        "9dfae945-726e-475c-9de1-e7f4cb3182ca",
        [{
            id: "8c656659-a8c0-4b95-b706-89f079f3208b",
            device_name: "Test device",
            last_heartbeat_at: "2026-07-29T08:00:00Z",
            last_successful_read_at: "2026-07-29T08:00:00Z",
        }],
        {},
        new Date("2026-07-29T09:03:00Z"),
    );
    if (events.length !== 1 || !events[0].idempotencyKey.includes("offline")) {
        throw new Error("offline event was not created");
    }
});
