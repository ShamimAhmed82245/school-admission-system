export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || "http://localhost:8080";

export const studentHeader = (studentId: string) => ({
  "X-Student-Id": studentId,
});

type ApiRequestOptions = {
  method?: "GET" | "POST" | "DELETE";
  body?: unknown;
  studentId?: string;
  signal?: AbortSignal;
};

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

function fallbackMessage(status: number) {
  if (status === 400) return "The request is invalid. Please check the information and try again.";
  if (status === 401 || status === 403)
    return "This operation is not permitted for the current student.";
  if (status === 404) return "The requested class or application could not be found.";
  if (status === 409)
    return "The request conflicts with the current application or seat availability.";
  if (status >= 500) return "The server is temporarily unavailable. Please try again.";
  return "The request could not be completed. Please try again.";
}

function extractMessage(payload: unknown, status: number) {
  if (!payload || typeof payload !== "object") return fallbackMessage(status);
  const data = payload as Record<string, unknown>;
  for (const key of ["message", "detail", "title"]) {
    if (typeof data[key] === "string" && data[key]) return data[key] as string;
  }
  if (data.errors && typeof data.errors === "object") {
    const messages = Object.values(data.errors as Record<string, unknown>)
      .flatMap((value) => (Array.isArray(value) ? value : [value]))
      .filter((value): value is string => typeof value === "string");
    if (messages.length > 0) return messages.join(" ");
  }
  return fallbackMessage(status);
}

export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (options.studentId) Object.assign(headers, studentHeader(options.studentId));
  if (options.body !== undefined) headers["Content-Type"] = "application/json";

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL.replace(/\/$/, "")}${path}`, {
      method: options.method ?? "GET",
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError(
      "Unable to connect to the admission server. Check the server connection and try again.",
      0,
    );
  }

  if (response.status === 204) return undefined as T;

  const isJson = response.headers.get("content-type")?.includes("application/json");
  const payload: unknown = isJson
    ? await response.json().catch(() => null)
    : await response.text().catch(() => "");

  if (!response.ok) throw new ApiError(extractMessage(payload, response.status), response.status);
  return payload as T;
}

export function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback;
}
