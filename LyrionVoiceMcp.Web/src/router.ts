import { createRouter, createWebHistory } from 'vue-router';
import OperationalHomeView from './features/operations/OperationalHomeView.vue';
import OperationalRecordListView from './features/operational-history/OperationalRecordListView.vue';
import OperationalRecordDetailView from './features/operational-history/OperationalRecordDetailView.vue';
import ScheduledJobsView from './features/operational-history/ScheduledJobsView.vue';
import ToolLogView from './features/tool-log/ToolLogView.vue';
import LicencesView from './features/licences/LicencesView.vue';
export const routes = [
  { path: '/', redirect: '/tool-calls' },
  { path: '/tool-calls', name: 'tool-calls', component: ToolLogView },
  { path: '/tool-calls/:id', name: 'tool-calls-detail', component: ToolLogView },
  { path: '/system', name: 'home', component: OperationalHomeView },
  { path: '/system/jobs', name: 'jobs', component: OperationalRecordListView, props: { kind: 'jobs' } },
  { path: '/system/jobs/:id', name: 'jobs-detail', component: OperationalRecordDetailView, props: { kind: 'jobs' } },
  { path: '/system/schedules', name: 'scheduled-jobs', component: ScheduledJobsView },
  { path: '/system/errors', name: 'errors', component: OperationalRecordListView, props: { kind: 'errors' } },
  { path: '/system/errors/:id', name: 'errors-detail', component: OperationalRecordDetailView, props: { kind: 'errors' } },
  { path: '/jobs', redirect: { name: 'jobs' } },
  { path: '/jobs/:id', redirect: { name: 'jobs-detail' } },
  { path: '/scheduled-jobs', redirect: { name: 'scheduled-jobs' } },
  { path: '/errors', redirect: { name: 'errors' } },
  { path: '/errors/:id', redirect: { name: 'errors-detail' } },
  { path: '/licences', name: 'licences', component: LicencesView },
  { path: '/:pathMatch(.*)*', redirect: '/tool-calls' }
];
export const router = createRouter({ history: createWebHistory(), routes });
