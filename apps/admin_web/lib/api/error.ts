/**
 * T031: ApiError — typed shape mapped from the backend's ProblemDetails
 * envelope (RFC 9457). Every Next.js Route Handler that proxies the
 * backend converts the response into either a typed result or an ApiError.
 */

export interface ApiErrorShape {
  status: number;
  type?: string;
  title?: string;
  detail?: string;
  /** ProblemDetails extension members — closed enums per spec 003 / 004 / etc. */
  reasonCode?: string;
  /** Field-level validation errors. */
  errors?: Record<string, string[]>;
  correlationId?: string;
}

export class ApiError extends Error {
  public readonly status: number;
  public readonly type?: string;
  public readonly reasonCode?: string;
  public readonly errors?: Record<string, string[]>;
  public readonly correlationId?: string;

  constructor(shape: ApiErrorShape) {
    super(shape.detail ?? shape.title ?? `HTTP ${shape.status}`);
    this.name = "ApiError";
    this.status = shape.status;
    this.type = shape.type;
    this.reasonCode = shape.reasonCode;
    this.errors = shape.errors;
    this.correlationId = shape.correlationId;
  }

  static async fromResponse(res: Response): Promise<ApiError> {
    let body: Partial<ApiErrorShape> = {};
    try {
      body = (await res.json()) as Partial<ApiErrorShape>;
    } catch {
      // non-JSON body
    }
    return new ApiError({
      status: res.status,
      type: body.type,
      title: body.title,
      detail: body.detail,
      reasonCode: body.reasonCode,
      errors: body.errors,
      correlationId: res.headers.get("x-correlation-id") ?? undefined,
    });
  }
}

export function isApiError(err: unknown): err is ApiError {
  return err instanceof ApiError;
}
