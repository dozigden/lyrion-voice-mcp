import { providerSearchFields } from './providers/bbcSounds';
import { array, boolean, nullable, number, object, optional, string, isRecord } from '../../shared/api/decoder';
import type { ToolCall } from '../operational-history/operationalHistoryApi';
const ns = nullable(string), nn = nullable(number);
const artist = object({ name: string, browseRef: string });
const album = object({ title: string, artist: ns, browseRef: string, playRef: string });
const track = object({ title: string, artist: ns, album: ns, rating: number, playRef: string });
const playlist = object({ title: string, browseRef: string, playRef: string });
const player = object({ id: string, name: string, poweredOn: boolean, mode: string, volume: nn, muted: nullable(boolean),
  nowPlaying: nullable(object({ title: string, artist: ns, album: ns, durationSeconds: nn, elapsedSeconds: nn })) });
const outcomeFields = { requestedItemCount: number, completedItemCount: number,
  skippedItems: array(object({ index: number, reason: string, message: string })), stateRefreshError: ns };
export const resultDecoders = {
  search: object({ guidance: string, exactArtistMatch: nullable(object({ name: string, discographyAlbumCount: nn, discographyBrowseRef: string })),
    artists: array(artist), albums: array(album), topTracks: array(track), tracks: array(track), playlists: array(playlist), ...providerSearchFields }),
  browse: object({ guidance: string, nextBrowseRef: ns, items: array(object({ kind: string, title: string, artist: ns, album: ns,
    browseRef: optional(string), playRef: optional(string), rating: optional(number) })) }),
  get_player_status: object({ players: array(player) }),
  control_player: object({ player }),
  get_queue: object({ player: string, currentIndex: nn,
    items: array(object({ index: number, title: string, artist: ns, album: ns, durationSeconds: nn })) }),
  manage_queue: object({ ...outcomeFields, player: string, queueLength: nn }),
  play: object({ ...outcomeFields, player: nullable(player) })
};
export type ToolName = keyof typeof resultDecoders;
export type ToolResults = { [K in ToolName]: ReturnType<typeof resultDecoders[K]> };
export type Player = ReturnType<typeof player>;
export type Outcome = Pick<ToolResults['play'], keyof typeof outcomeFields>;
type RenderedResult = { [K in ToolName]: { tool: K; data: ToolResults[K] } }[ToolName];
function decodeResult(tool: string, value: unknown): RenderedResult | null {
  // Explicit branches retain the relationship between the discriminator and decoded data.
  try {
    switch (tool) {
      case 'search': return { tool, data: resultDecoders.search(value) };
      case 'browse': return { tool, data: resultDecoders.browse(value) };
      case 'get_player_status': return { tool, data: resultDecoders.get_player_status(value) };
      case 'control_player': return { tool, data: resultDecoders.control_player(value) };
      case 'get_queue': return { tool, data: resultDecoders.get_queue(value) };
      case 'manage_queue': return { tool, data: resultDecoders.manage_queue(value) };
      case 'play': return { tool, data: resultDecoders.play(value) };
      default: return null;
    }
  } catch { return null; }
}
function parse(value: string | null): unknown {
  if (value === null) return undefined;
  try { return JSON.parse(value); } catch { return undefined; }
}
function equivalent(a: unknown, b: unknown, depth = 0): boolean {
  if (depth > 30) return false;
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((item, i) => equivalent(item, b[i], depth + 1));
  if (isRecord(a) && isRecord(b)) {
    return Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(key => key in b && equivalent(a[key], b[key], depth + 1));
  }
  return false;
}
export function presentCall(call: ToolCall) {
  const args = parse(call.argumentsJson);
  const envelope = parse(call.resultJson);
  const content = isRecord(envelope) && Array.isArray(envelope.content) ? envelope.content : [];
  const texts = content.flatMap(item => isRecord(item) && item.type === 'text' && typeof item.text === 'string' ? [item.text] : []);
  // The SDK records explicit null when a result contains text only.
  let structured: unknown = isRecord(envelope) ? envelope.structuredContent ?? undefined : undefined;
  if (structured === undefined) {
    structured = texts.map(parse).find(value => decodeResult(call.toolName, value) !== null);
  }
  const result = call.resultTruncated ? null : decodeResult(call.toolName, structured);
  const distinct = texts.filter(text => structured === undefined || !equivalent(parse(text), structured));
  const messages = distinct.filter(text => parse(text) === undefined);
  const structuredMessages = distinct.flatMap(text => { const value = parse(text); return value === undefined ? [] : [value]; });
  const requests = isRecord(args) && !call.argumentsTruncated ? Object.entries(args).map(([key, value]) => ({ key, value })) : [];
  const references: { path: string; value: string }[] = [];
  function visit(value: unknown, path: string, depth = 0) {
    if (depth > 30) return;
    if (Array.isArray(value)) value.forEach((item, index) => visit(item, `${path}[${index}]`, depth + 1));
    else if (isRecord(value)) {
      for (const [key, item] of Object.entries(value)) {
        const next = path ? `${path}.${key}` : key;
        if ((key.endsWith('Ref') || key === 'guidance') && typeof item === 'string') references.push({ path: next, value: item });
        else visit(item, next, depth + 1);
      }
    }
  }
  visit(structured, 'result');
  return { result, requests, references, messages, structuredMessages,
    requestUnavailable: !isRecord(args) || call.argumentsTruncated,
    hasOtherContent: content.some(item => !isRecord(item) || item.type !== 'text'),
    isError: isRecord(envelope) && envelope.isError === true,
    fallback: call.resultJson !== null && result === null && (structured !== undefined || messages.length === 0)
  };
}
