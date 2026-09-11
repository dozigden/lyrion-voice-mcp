import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import ToolCallDetail from './ToolCallDetail.vue';
import { presentCall, type ToolName } from './toolResults';
import { call, results } from './toolLogFixtures';
describe('recorded MCP tool presentation', () => {
  it.each(Object.keys(results) as ToolName[])('renders %s without JSON in Details', tool => {
    const wrapper = mount(ToolCallDetail, { props: { call: call(tool, tool, results[tool]) } });
    expect(wrapper.find('pre').exists()).toBe(false);
    expect(wrapper.text()).not.toContain('does not match a supported presentation');
    expect(wrapper.get('.call-heading .tool-icon').classes()).toContain(`tool-icon--${tool}`);
    const headings = wrapper.findAll('.result-section h3');
    expect(headings.length).toBeGreaterThan(1);
    expect(headings[0]!.text()).not.toBe('Other');
    expect(headings.at(-1)!.text()).toBe('Other');
  });
  it('lays out short groups horizontally and main tracks vertically, retaining ratings and references', () => {
    const wrapper = mount(ToolCallDetail, { props: { call: call() } });
    expect(wrapper.findAll('.media-list.horizontal')).toHaveLength(2);
    expect(wrapper.findAll('.media-list.vertical')).toHaveLength(1);
    expect(wrapper.text()).toContain('4.5');
    expect(wrapper.text()).toContain('browse-discography');
    expect(wrapper.text()).toContain('8 albums in discography');
    expect(wrapper.findAll('.result-section h3')[0]!.text()).toBe('Exact artist');
  });
  it('shows skipped items and state refresh errors even when the call succeeded', () => {
    const wrapper = mount(ToolCallDetail, { props: { call: call('play', 'play-fiction', results.play) } });
    expect(wrapper.text()).toContain('1 of 2 requested items completed');
    expect(wrapper.text()).toContain('Media unavailable');
    expect(wrapper.text()).toContain('Player state could not be refreshed.');
    expect(wrapper.text()).toContain('Refreshed player state was not returned.');
  });
  it('shows captured reference names in request order with an honest unresolved fallback', () => {
    const record = { ...call('play', 'labelled-play', results.play),
      argumentsJson: JSON.stringify({ player: 'Studio player', items: ['track-one', 'unknown-two'] }),
      referenceSnapshots: [
        { argumentPath: 'items[0]', reference: 'track-one', displayMetadata: { kind: 'track', title: 'Paper Satellites', artist: 'The Lantern Hours', album: 'Northern Windows', isContinuation: false } },
        { argumentPath: 'items[1]', reference: 'unknown-two', displayMetadata: null }
      ] };

    const wrapper = mount(ToolCallDetail, { props: { call: record } });

    expect(wrapper.find('.references.horizontal').exists()).toBe(true);
    expect(wrapper.findAll('.references li').map(item => item.text())).toEqual([
      expect.stringContaining('Paper Satellites'),
      expect.stringContaining('Name not captured')
    ]);
    expect(wrapper.text()).toContain('track-one');
    expect(wrapper.text()).toContain('unknown-two');
  });
  it('renders more than five captured references vertically', () => {
    const references = Array.from({ length: 6 }, (_, index) => ({
      argumentPath: `items[${index}]`, reference: `track-${index}`,
      displayMetadata: { kind: 'track', title: `Fictional Track ${index + 1}`, artist: null, album: null, isContinuation: false }
    }));
    const record = { ...call('play', 'long-play', results.play),
      argumentsJson: JSON.stringify({ player: 'Studio player', items: references.map(item => item.reference) }),
      referenceSnapshots: references };

    const wrapper = mount(ToolCallDetail, { props: { call: record } });

    expect(wrapper.find('.references.vertical').exists()).toBe(true);
    expect(wrapper.findAll('.references li')).toHaveLength(6);
  });
  it('falls back to original arguments for history without usable snapshots', () => {
    const historical = { ...call('browse', 'historical-browse', results.browse),
      argumentsJson: JSON.stringify({ browseRef: 'album_historical' }), referenceSnapshots: null };
    const truncated = { ...historical, id: 'truncated-labels', referenceSnapshotsTruncated: true };

    const historicalWrapper = mount(ToolCallDetail, { props: { call: historical } });
    const truncatedWrapper = mount(ToolCallDetail, { props: { call: truncated } });

    expect(historicalWrapper.text()).toContain('album_historical');
    expect(truncatedWrapper.text()).toContain('album_historical');
    expect(truncatedWrapper.text()).toContain('Captured reference labels were truncated.');
  });
  it('deduplicates equivalent structured text but preserves distinct messages and raw recording', async () => {
    const record = call();
    const data = JSON.parse(record.resultJson!);
    data.content.push({ type: 'text', text: 'Some results were unavailable.' });
    record.resultJson = JSON.stringify(data);
    const wrapper = mount(ToolCallDetail, { props: { call: record } });
    expect(wrapper.findAll('.message').map(item => item.text())).toEqual(['Some results were unavailable.']);
    await wrapper.get('#call-tab-raw').trigger('click');
    expect(wrapper.findAll('pre')).toHaveLength(2);
    expect(wrapper.text()).toContain('structuredContent');
  });
  it.each([undefined, null])('presents text-only errors with absent or null structured content', structuredContent => {
    const record = { ...call(), status: 'tool_error', errorMessage: 'Tool returned an error result.', resultJson: JSON.stringify({ isError: true, structuredContent, content: [{ type: 'text', text: 'No player matched the request.' }] }) };
    expect(presentCall(record).messages).toEqual(['No player matched the request.']);
    const wrapper = mount(ToolCallDetail, { props: { call: record } });
    expect(wrapper.text()).toContain('No player matched the request.');
    expect(wrapper.text()).not.toContain('does not match');
  });
  it.each([
    { ...call('future_tool'), resultJson: '{"structuredContent":{"future":true}}' },
    { ...call(), resultJson: '{broken' },
    call('search', 'invalid-nested', { ...results.search, tracks: [{ ...results.search.tracks[0], rating: 'high' }] }),
    { ...call(), resultTruncated: true, resultJson: '{"truncated":true,"prefix":"saved portion"}' }
  ])('falls back safely for an unsupported or incomplete record', record => {
    const wrapper = mount(ToolCallDetail, { props: { call: record } });
    expect(wrapper.text()).toContain('Inspect Raw JSON');
    expect(wrapper.find('pre').exists()).toBe(false);
  });
  it('uses a neutral icon for an unknown historical tool', () => {
    const wrapper = mount(ToolCallDetail, { props: { call: call('future_tool') } });
    expect(wrapper.get('.call-heading .tool-icon').classes()).toContain('tool-icon--unknown');
  });
  it('distinguishes truncation and missing results, and resets the tab for a different call', async () => {
    const wrapper = mount(ToolCallDetail, { props: { call: { ...call(), argumentsTruncated: true, resultJson: null } } });
    expect(wrapper.text()).toContain('truncated before storage');
    expect(wrapper.text()).toContain('No result recorded.');
    await wrapper.get('#call-tab-raw').trigger('click');
    await wrapper.setProps({ call: call('search', 'another-call') });
    expect(wrapper.get('#call-tab-details').attributes('aria-selected')).toBe('true');
  });
});
