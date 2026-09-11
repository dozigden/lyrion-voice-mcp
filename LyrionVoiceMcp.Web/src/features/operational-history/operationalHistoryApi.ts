import { array, boolean, date, nullable, number, object, oneOf, string } from '../../shared/api/decoder';
import { request } from '../../shared/api/http';

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
export interface ScheduledJobEditableConfiguration {
  kind: 'interval' | 'daily_time'; configuredEnabled: boolean;
  intervalMinutes: number | null; dailyTime: string | null;
}
export interface ScheduledJob {
  name: string; displayName: string; enabled: boolean; cronExpression: string; timeZoneId: string;
  lastEvaluatedAt: string | null; nextOccurrenceAt: string | null;
  currentJob: ScheduledJobRun | null; lastStartedJob: ScheduledJobRun | null;
  editableConfiguration: ScheduledJobEditableConfiguration | null;
}
export interface ScheduledJobConfigurationUpdate {
  enabled: boolean; intervalMinutes: number | null; dailyTime: string | null;
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
  referenceSnapshots: ToolCallReferenceSnapshot[] | null; referenceSnapshotsTruncated: boolean;
  resultJson: string | null; resultTruncated: boolean; errorMessage: string | null;
  traceIdentifier: string | null; errorLogId: number | null;
}
export interface ToolCallReferenceSnapshot {
  argumentPath: string; reference: string; displayMetadata: ReferenceDisplayMetadata | null;
}
export interface ReferenceDisplayMetadata {
  kind: string; title: string; artist: string | null; album: string | null; isContinuation: boolean;
}
export interface ToolCallSummary {
  id: string; toolName: string; status: string; startedAt: string; completedAt: string | null;
  durationMilliseconds: number | null; traceIdentifier: string | null; errorLogId: number | null;
  requestSummary: string | null;
}
export interface ToolCallPage { items: ToolCallSummary[]; total: number; offset: number; limit: number; retentionDays: number; }

const ns = nullable(string), nn = nullable(number), nd = nullable(date);
const jobFields = { id: number, type: string, status: string, runAfter: date, startedAt: nd,
  completedAt: nd, correlationId: ns, createdAt: date, updatedAt: date };
const job = object({ ...jobFields, payloadJson: string, resultJson: string, errorMessage: ns });
const jobDetails = object({ job, logs: array(object({ id: number, level: string, message: string, dataJson: ns, loggedAt: date })) });
const pageFields = { total: number, offset: number, limit: number, retentionDays: number };
const jobPage = object({ ...pageFields, items: array(object(jobFields)) });
const run = object({ id: number, status: string, startedAt: nd });
const schedule = object({ name: string, displayName: string, enabled: boolean, cronExpression: string, timeZoneId: string,
  lastEvaluatedAt: nd, nextOccurrenceAt: nd, currentJob: nullable(run), lastStartedJob: nullable(run),
  editableConfiguration: nullable(object({ kind: oneOf('interval', 'daily_time'), configuredEnabled: boolean,
    intervalMinutes: nn, dailyTime: ns })) });
const errorFields = { id: number, occurredAt: date, source: string, area: string, exceptionType: string,
  message: string, traceIdentifier: ns, jobId: nn };
const errorLog = object({ ...errorFields, reportId: ns, stackTrace: ns, requestMethod: ns, requestPath: ns, contextJson: ns, createdAt: date });
const errorPage = object({ ...pageFields, items: array(object(errorFields)) });
const toolFields = { id: string, toolName: string, status: string, startedAt: date, completedAt: nd,
  durationMilliseconds: nn, traceIdentifier: ns, errorLogId: nn };
const toolCall = object({ ...toolFields, argumentsJson: string, argumentsTruncated: boolean,
  referenceSnapshots: nullable(array(object({ argumentPath: string, reference: string,
    displayMetadata: nullable(object({ kind: string, title: string, artist: ns, album: ns, isContinuation: boolean })) }))),
  referenceSnapshotsTruncated: boolean,
  resultJson: ns, resultTruncated: boolean, errorMessage: ns });
const toolPage = object({ ...pageFields, items: array(object({ ...toolFields, requestSummary: ns })) });

export const listJobs = (query = '', signal?: AbortSignal): Promise<JobPage> => request(`/api/jobs${query}`, jobPage, { signal });
export const getJob = (id: string, signal?: AbortSignal): Promise<JobDetails> => request(`/api/jobs/${encodeURIComponent(id)}`, jobDetails, { signal });
export const cancelJob = (id: number, signal?: AbortSignal): Promise<Job> => request(`/api/jobs/${id}/cancel`, job, { method: 'POST', signal });
export const listSchedules = (signal?: AbortSignal): Promise<ScheduledJob[]> => request('/api/scheduled-jobs', array(schedule), { signal });
export const updateScheduleConfiguration = (name: string, update: ScheduledJobConfigurationUpdate, signal?: AbortSignal): Promise<ScheduledJob> =>
  request(`/api/scheduled-jobs/${encodeURIComponent(name)}/configuration`, schedule, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(update), signal
  });
export const runSchedule = (name: string, signal?: AbortSignal) => request(`/api/scheduled-jobs/${encodeURIComponent(name)}/run`,
  object({ enqueuedCount: number, jobIds: array(number) }), { method: 'POST', signal });
export const listErrors = (query = '', signal?: AbortSignal): Promise<ErrorLogPage> => request(`/api/error-logs${query}`, errorPage, { signal });
export const getError = (id: string, signal?: AbortSignal): Promise<ErrorLog> => request(`/api/error-logs/${encodeURIComponent(id)}`, errorLog, { signal });
export const listToolCalls = (query = '', signal?: AbortSignal): Promise<ToolCallPage> => request(`/api/tool-calls${query}`, toolPage, { signal });
export const getToolCall = (id: string, signal?: AbortSignal): Promise<ToolCall> => request(`/api/tool-calls/${encodeURIComponent(id)}`, toolCall, { signal });
