import { adminRestFetch, JsonRecord } from "./http.ts";

export interface UsageRow extends JsonRecord {
    short_term_remaining?: number | null;
    short_term_reset_at?: string | null;
    weekly_remaining?: number | null;
    weekly_reset_at?: string | null;
    captured_at?: string;
    received_at?: string;
    source_freshness?: string;
}

export async function sendTelegramMessage(
    chatId: string,
    text: string,
): Promise<boolean> {
    const botToken = Deno.env.get("TELEGRAM_BOT_TOKEN");
    if (!botToken) {
        throw new Error("TELEGRAM_BOT_TOKEN is unavailable");
    }

    try {
        const response = await fetch(
            `https://api.telegram.org/bot${botToken}/sendMessage`,
            {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({
                    chat_id: chatId,
                    text,
                    disable_web_page_preview: true,
                }),
            },
        );
        return response.ok;
    } catch {
        return false;
    }
}

export function formatUsageMessage(
    row: UsageRow | null,
    now = new Date(),
): string {
    if (!row) {
        return "🤖 Codex Usage\n\nUsage is unavailable. No device reading has been synchronized yet.";
    }

    const captured = validDate(row.captured_at);
    const stale = !captured ||
        now.getTime() - captured.getTime() > 15 * 60 * 1000 ||
        row.source_freshness !== "fresh";
    const lines = [
        "🤖 Codex Usage",
        "",
        `Short-term: ${formatPercent(row.short_term_remaining)}`,
        formatReset("Resets", row.short_term_reset_at, now),
        "",
        `Weekly: ${formatPercent(row.weekly_remaining)}`,
        formatReset("Resets", row.weekly_reset_at, now),
    ];

    if (stale) {
        lines.push(
            "",
            "⚠️ This is the last known reading.",
            `Last synchronized: ${formatAge(captured, now)}`,
            "Device status: Offline or stale",
        );
    } else {
        lines.push(
            "",
            "Device: Online",
            `Last synchronized: ${formatAge(captured, now)}`,
        );
    }

    return lines.filter((line) => line !== "").join("\n").replace(
        /\n{3,}/g,
        "\n\n",
    );
}

export async function fetchLatestUsage(
    profileId: string,
): Promise<UsageRow | null> {
    const query = new URLSearchParams({
        profile_id: `eq.${profileId}`,
        select:
            "short_term_remaining,short_term_reset_at,weekly_remaining,weekly_reset_at,captured_at,received_at,source_freshness",
        order: "captured_at.desc",
        limit: "1",
    });
    const response = await adminRestFetch(`/rest/v1/latest_usage?${query}`);
    if (!response.ok) {
        throw new Error("latest_usage_query_failed");
    }

    const rows = await response.json() as UsageRow[];
    return rows[0] ?? null;
}

function formatPercent(value: unknown): string {
    return typeof value === "number" && Number.isFinite(value)
        ? `${Math.round(value * 10) / 10}% remaining`
        : "unavailable";
}

function formatReset(label: string, value: unknown, now: Date): string {
    const reset = validDate(value);
    if (!reset) {
        return `${label}: unavailable`;
    }

    const milliseconds = reset.getTime() - now.getTime();
    if (milliseconds <= 0) {
        return `${label}: known reset time has arrived; awaiting confirmation`;
    }

    const totalMinutes = Math.ceil(milliseconds / 60000);
    if (totalMinutes < 60) {
        return `${label} in: ${totalMinutes}m`;
    }

    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;
    return `${label} in: ${hours}h ${minutes}m`;
}

function formatAge(value: Date | null, now: Date): string {
    if (!value) {
        return "unknown";
    }

    const seconds = Math.max(
        0,
        Math.floor((now.getTime() - value.getTime()) / 1000),
    );
    if (seconds < 60) {
        return `${seconds} seconds ago`;
    }

    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) {
        return `${minutes} minutes ago`;
    }

    return `${Math.floor(minutes / 60)} hours ago`;
}

function validDate(value: unknown): Date | null {
    if (typeof value !== "string") {
        return null;
    }

    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
}
