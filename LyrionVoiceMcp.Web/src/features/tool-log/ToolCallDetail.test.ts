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
    expect(wrapper.findAll('.result-section').length).toBeGreaterThan(0);
  });
  it('lays out short groups horizontally and main tracks vertically, retaining ratings and references', () => {
    const wrapper = mount(ToolCallDetail, { props: { call: call() } });
    expect(wrapper.findAll('.media-list.horizontal')).toHaveLength(2);
    expect(wrapper.findAll('.media-list.vertical')).toHaveLength(1);
    expect(wrapper.text()).toContain('4.5');
    expect(wrapper.text()).toContain('browse-discography');
    expect(wrapper.text()).toContain('8 albums in discography');
  });
  it('shows skipped items and state refresh errors even when the call succeeded', () => {
    const wrapper = mount(ToolCallDetail, { props: { call: call('play', 'play-fiction', results.play) } });
    expect(wrapper.text()).toContain('1 of 2 requested items completed');
    expect(wrapper.text()).toContain('Media unavailable');
    expect(wrapper.text()).toContain('Player state could not be refreshed.');
    expect(wrapper.text()).toContain('Refreshed player state was not returned.');
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
  it('distinguishes truncation and missing results, and resets the tab for a different call', async () => {
    const wrapper = mount(ToolCallDetail, { props: { call: { ...call(), argumentsTruncated: true, resultJson: null } } });
    expect(wrapper.text()).toContain('truncated before storage');
    expect(wrapper.text()).toContain('No result recorded.');
    await wrapper.get('#call-tab-raw').trigger('click');
    await wrapper.setProps({ call: call('search', 'another-call') });
    expect(wrapper.get('#call-tab-details').attributes('aria-selected')).toBe('true');
  });
});
