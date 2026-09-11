// Fictional contract fixtures; never use live call history here.
import type { ToolCall, ToolCallSummary } from '../operational-history/operationalHistoryApi';
import type { ToolResults } from './toolResults';
export const player = { id: 'player-fiction', name: 'Studio player', poweredOn: true, mode: 'playing', volume: 35, muted: false,
  nowPlaying: { title: 'Paper Satellites', artist: 'The Lantern Hours', album: 'Northern Windows', durationSeconds: 230, elapsedSeconds: 42 } };
const track = { title: 'Paper Satellites', artist: 'The Lantern Hours', album: 'Northern Windows', rating: 4.5, playRef: 'play-fiction' };
export const results: ToolResults = {
  search: { guidance: 'Use recorded references to continue.', exactArtistMatch: { name: 'The Lantern Hours', discographyAlbumCount: 8, discographyBrowseRef: 'browse-discography' }, artists: [],
    albums: Array.from({ length: 5 }, (_, i) => ({ title: ['Northern Windows', 'A Map of Quiet Places', 'The Last Harbour', 'Signals in the Rain', 'Small Hours'][i]!, artist: 'The Lantern Hours', browseRef: `browse-album-${i}`, playRef: `play-album-${i}` })),
    topTracks: Array.from({ length: 5 }, (_, i) => ({ ...track, title: ['Paper Satellites', 'Open Water', 'The Long Way Home', 'Signals', 'Before the Morning'][i]! })),
    tracks: Array.from({ length: 18 }, (_, i) => ({ ...track, title: `Northern Sketch ${i + 1}`, playRef: `play-track-${i}` })), playlists: [] },
  browse: { guidance: 'Browse these recorded items.', nextBrowseRef: 'next-fiction', items: [{ rating: undefined, kind: 'album', title: 'Northern Windows', artist: 'The Lantern Hours', album: null, browseRef: 'browse-fiction', playRef: 'play-fiction' }] },
  get_player_status: { players: [player] }, control_player: { player },
  get_queue: { player: 'player-fiction', currentIndex: 0, items: [{ index: 0, title: 'Paper Satellites', artist: 'The Lantern Hours', album: 'Northern Windows', durationSeconds: 230 }] },
  manage_queue: { player: 'player-fiction', queueLength: 2, requestedItemCount: 3, completedItemCount: 2, skippedItems: [{ index: 2, reason: 'invalid_reference', message: 'The saved reference expired.' }], stateRefreshError: null },
  play: { player: null, requestedItemCount: 2, completedItemCount: 1, skippedItems: [{ index: 1, reason: 'media_unavailable', message: 'This item was unavailable.' }], stateRefreshError: 'Player state could not be refreshed.' }
};
export function call(toolName = 'search', id = 'call-fiction', structured: unknown = results.search): ToolCall {
  return { id, toolName, status: 'succeeded', startedAt: '2026-01-02T13:04:05Z', completedAt: '2026-01-02T13:04:06Z', durationMilliseconds: 420,
    argumentsJson: JSON.stringify({ name: 'The Lantern Hours' }), argumentsTruncated: false,
    resultJson: JSON.stringify({ structuredContent: structured, content: [{ type: 'text', text: JSON.stringify(structured) }] }),
    resultTruncated: false, errorMessage: null, traceIdentifier: 'trace-fiction', errorLogId: null };
}
export function summary(id: string, toolName = 'search'): ToolCallSummary {
  return { id, toolName, status: 'succeeded', startedAt: '2026-01-02T13:04:05Z', completedAt: '2026-01-02T13:04:06Z', durationMilliseconds: 420, traceIdentifier: null, errorLogId: null, requestSummary: 'The Lantern Hours' };
}
