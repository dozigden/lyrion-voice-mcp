// Keep these choices aligned with JobTypes, JobStatus, ErrorLogSources and ErrorLogAreas.
export const jobTypes = [
  'catalogue.change-check',
  'catalogue.refresh',
  'search-index.rebuild',
  'error-log.purge',
  'job-history.purge',
  'tool-call-history.purge'
];
export const jobStatuses = ['pending', 'running', 'completed', 'failed', 'cancelled'];
export const errorSources = ['backend', 'mcp'];
export const errorAreas = ['api-request', 'job-runner', 'job-scheduler', 'mcp-tool-call'];
