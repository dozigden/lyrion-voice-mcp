export type Decoder<T> = (value: unknown) => T;
export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function invalid(): never { throw new Error('Invalid contract value.'); }
export const string: Decoder<string> = value => typeof value === 'string' ? value : invalid();
export const number: Decoder<number> = value => typeof value === 'number' && Number.isFinite(value) ? value : invalid();
export const boolean: Decoder<boolean> = value => typeof value === 'boolean' ? value : invalid();
export const date: Decoder<string> = value => {
  const text = string(value);
  return Number.isFinite(Date.parse(text)) ? text : invalid();
};
export const nullable = <T>(decode: Decoder<T>): Decoder<T | null> => value => value === null ? null : decode(value);
export const optional = <T>(decode: Decoder<T>): Decoder<T | undefined> => value => value === undefined ? undefined : decode(value);
export const array = <T>(decode: Decoder<T>): Decoder<T[]> => value => Array.isArray(value) ? value.map(decode) : invalid();
export const oneOf = <T extends string>(...values: T[]): Decoder<T> => value => {
  const found = values.find(item => item === value);
  return found === undefined ? invalid() : found;
};
export function object<S extends Record<string, Decoder<unknown>>>(shape: S): Decoder<{ [K in keyof S]: ReturnType<S[K]> }> {
  return value => {
    if (!isRecord(value)) return invalid();
    // Every returned property is constructed through its field decoder. Unknown fields are ignored.
    const result: Record<string, unknown> = {};
    for (const key of Object.keys(shape)) result[key] = shape[key]!(value[key]);
    return result as { [K in keyof S]: ReturnType<S[K]> };
  };
}
