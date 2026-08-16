export interface Job {
  id: number; type: string; status: string; runAfter: string; payloadJson: string;
  resultJson: string; errorMessage: string | null; startedAt: string | null;
  completedAt: string | null; correlationId: string | null; createdAt: string; updatedAt: string;
}
export interface JobSummary {
  id: number; type: string; status: string; runAfter: string; startedAt: string | null;
  completedAt: string | null; correlationId: string | null; createdAt: string; updatedAt: string;
}
export interface JobLog { id: number; level: string; message: string; dataJson: string | null; loggedAt: string; }
export interface JobDetails { job: Job; logs: JobLog[]; }
export interface JobPage { items: JobSummary[]; total: number; offset: number; limit: number; retentionDays: number; }
export interface ScheduledJobRun { id: number; status: string; startedAt: string | null; }
export interface ScheduledJob {
  name: string; displayName: string; enabled: boolean; cronExpression: string; timeZoneId: string;
  lastEvaluatedAt: string | null; nextOccurrenceAt: string | null;
  currentJob: ScheduledJobRun | null; lastStartedJob: ScheduledJobRun | null;
}
export interface ErrorLog {
  id: number; reportId: string | null; occurredAt: string; source: string; area: string;
  exceptionType: string; message: string; stackTrace: string | null; traceIdentifier: string | null;
  requestMethod: string | null; requestPath: string | null; jobId: number | null;
  contextJson: string | null; createdAt: string;
}
export interface ErrorLogSummary {
  id: number; occurredAt: string; source: string; area: string; exceptionType: string;
  message: string; traceIdentifier: string | null; jobId: number | null;
}
export interface ErrorLogPage { items: ErrorLogSummary[]; total: number; offset: number; limit: number; retentionDays: number; }
export interface ToolCall {
  id: string; toolName: string; status: string; startedAt: string; completedAt: string | null;
  durationMilliseconds: number | null; argumentsJson: string; argumentsTruncated: boolean;
  resultJson: string | null; resultTruncated: boolean; errorMessage: string | null;
  traceIdentifier: string | null; errorLogId: number | null;
}
export interface ToolCallSummary {
  id: string; toolName: string; status: string; startedAt: string; completedAt: string | null;
  durationMilliseconds: number | null; traceIdentifier: string | null; errorLogId: number | null;
}
export interface ToolCallPage { items: ToolCallSummary[]; total: number; offset: number; limit: number; retentionDays: number; }

export const listJobs = (query = '') => get<JobPage>(`/api/jobs${query}`);
export const getJob = (id: string) => get<JobDetails>(`/api/jobs/${encodeURIComponent(id)}`);
export const cancelJob = (id: number) => post<Job>(`/api/jobs/${id}/cancel`);
export const listSchedules = () => get<ScheduledJob[]>('/api/scheduled-jobs');
export const runSchedule = (name: string) => post(`/api/scheduled-jobs/${encodeURIComponent(name)}/run`);
export const listErrors = (query = '') => get<ErrorLogPage>(`/api/error-logs${query}`);
export const getError = (id: string) => get<ErrorLog>(`/api/error-logs/${encodeURIComponent(id)}`);
export const listToolCalls = (query = '') => get<ToolCallPage>(`/api/tool-calls${query}`);
export const getToolCall = (id: string) => get<ToolCall>(`/api/tool-calls/${encodeURIComponent(id)}`);

async function get<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: 'application/json' } });
  if (!response.ok) throw new Error(`${url} returned HTTP ${response.status}.`);
  return await response.json() as T;
}

async function post<T = unknown>(url: string): Promise<T> {
  const response = await fetch(url, { method: 'POST', headers: { Accept: 'application/json' } });
  if (!response.ok) throw new Error(`${url} returned HTTP ${response.status}.`);
  return await response.json() as T;
}
