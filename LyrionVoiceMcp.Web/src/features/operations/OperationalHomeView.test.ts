import { createPinia, setActivePinia } from 'pinia';
import { flushPromises, mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './operationsApi';
import OperationalHomeView from './OperationalHomeView.vue';

describe('OperationalHomeView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  it('shows a healthy build', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' });
    vi.spyOn(api, 'getVersion').mockResolvedValue({
      version: '0.1.0',
      channel: 'test',
      build: 'ui-test',
      commit: 'abcdef0'
    });
    vi.spyOn(api, 'getLmsConnection').mockResolvedValue({
      status: 'online',
      serverId: 'development',
      baseUrl: 'http://music.test:9000',
      serverVersion: '9.0.1',
      message: 'Connected.'
    });

    // Act
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('Online');
    expect(wrapper.text()).toContain('0.1.0');
    expect(wrapper.text()).toContain('/mcp');
    expect(wrapper.text()).toContain('development');
    expect(wrapper.text()).toContain('LMS 9.0.1');
    expect(wrapper.text()).toContain('Trusted network only');
  });

  it('shows an unavailable service', async () => {
    // Arrange
    vi.spyOn(api, 'getHealth').mockRejectedValue(new Error('API unavailable.'));
    vi.spyOn(api, 'getVersion').mockRejectedValue(new Error('API unavailable.'));
    vi.spyOn(api, 'getLmsConnection').mockRejectedValue(new Error('API unavailable.'));

    // Act
    const wrapper = mount(OperationalHomeView, {
      global: { plugins: [createPinia()] }
    });
    await flushPromises();

    // Assert
    expect(wrapper.text()).toContain('Unavailable');
    expect(wrapper.text()).toContain('API unavailable.');
  });
});
