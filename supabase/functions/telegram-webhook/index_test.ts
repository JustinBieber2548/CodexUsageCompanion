import { formatUsageMessage } from "../_shared/telegram.ts";
import { handleRequest } from "./index.ts";

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

Deno.test(
    "webhook ignores unauthorized chat, deduplicates, and releases API failures for retry",
    async () => {
        const originalFetch = globalThis.fetch;
        const environment = [
            "SUPABASE_URL",
            "SUPABASE_SECRET_KEYS",
            "TELEGRAM_BOT_TOKEN",
            "TELEGRAM_WEBHOOK_SECRET",
            "AUTHORIZED_PROFILE_ID",
            "AUTHORIZED_TELEGRAM_CHAT_ID",
        ];
        const previous = new Map(
            environment.map((name) => [name, Deno.env.get(name)]),
        );
        try {
            Deno.env.set("SUPABASE_URL", "https://project.supabase.co");
            Deno.env.set(
                "SUPABASE_SECRET_KEYS",
                JSON.stringify({ default: "sb_secret_test" }),
            );
            Deno.env.set("TELEGRAM_BOT_TOKEN", "test-bot-token");
            Deno.env.set("TELEGRAM_WEBHOOK_SECRET", "webhook-secret");
            Deno.env.set(
                "AUTHORIZED_PROFILE_ID",
                "00000000-0000-0000-0000-000000000001",
            );
            Deno.env.set("AUTHORIZED_TELEGRAM_CHAT_ID", "123");

            let mode: "unauthorized" | "duplicate" | "retry" = "unauthorized";
            let telegramAttempts = 0;
            globalThis.fetch = (
                input: string | URL | Request,
                init?: RequestInit,
            ): Promise<Response> => {
                const url = input instanceof Request
                    ? input.url
                    : input.toString();
                if (url.includes("processed_telegram_updates")) {
                    if (init?.method === "DELETE") {
                        return Promise.resolve(
                            new Response(null, {
                                status: 204,
                            }),
                        );
                    }

                    return Promise.resolve(Response.json(
                        mode === "duplicate" ? [] : [{ update_id: 1 }],
                    ));
                }

                if (url.includes("api.telegram.org")) {
                    telegramAttempts++;
                    return Promise.resolve(
                        new Response(
                            "{}",
                            {
                                status: mode === "retry" &&
                                        telegramAttempts === 1
                                    ? 500
                                    : 200,
                            },
                        ),
                    );
                }

                throw new Error(`Unexpected test fetch: ${url}`);
            };

            const unauthorized = await handleRequest(webhookRequest(1, "999"));
            assertEquals(unauthorized.status, 200);
            assertIncludes(await unauthorized.text(), '"ignored":true');
            assertEquals(telegramAttempts, 0);

            mode = "duplicate";
            const duplicate = await handleRequest(webhookRequest(2, "123"));
            assertEquals(duplicate.status, 200);
            assertIncludes(await duplicate.text(), '"duplicate":true');
            assertEquals(telegramAttempts, 0);

            mode = "retry";
            const failed = await handleRequest(webhookRequest(3, "123"));
            assertEquals(failed.status, 500);
            const retried = await handleRequest(webhookRequest(3, "123"));
            assertEquals(retried.status, 200);
            assertEquals(telegramAttempts, 2);
        } finally {
            globalThis.fetch = originalFetch;
            for (const [name, value] of previous) {
                if (value === undefined) {
                    Deno.env.delete(name);
                } else {
                    Deno.env.set(name, value);
                }
            }
        }
    },
);

function webhookRequest(updateId: number, chatId: string): Request {
    return new Request(
        "https://project.supabase.co/functions/v1/telegram-webhook",
        {
            method: "POST",
            headers: {
                "content-type": "application/json",
                "x-telegram-bot-api-secret-token": "webhook-secret",
            },
            body: JSON.stringify({
                update_id: updateId,
                message: { chat: { id: chatId }, text: "/start" },
            }),
        },
    );
}

function assertIncludes(value: string, expected: string): void {
    if (!value.includes(expected)) {
        throw new Error(`Expected '${value}' to include '${expected}'`);
    }
}

function assertEquals<T>(actual: T, expected: T): void {
    if (actual !== expected) {
        throw new Error(
            `Expected '${String(actual)}' to equal '${String(expected)}'`,
        );
    }
}
