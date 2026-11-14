export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string, public revision?: string) { super(message); }
}
export const invalid = (message: string): never => { throw new ApiError(400, 'invalid_document', message); };
