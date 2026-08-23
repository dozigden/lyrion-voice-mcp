import { createRouter, createWebHistory } from 'vue-router';
import OperationalHomeView from './features/operations/OperationalHomeView.vue';
import SearchObservationDetailView from './features/search-observations/SearchObservationDetailView.vue';
import SearchObservationListView from './features/search-observations/SearchObservationListView.vue';
import OperationalRecordListView from './features/operational-history/OperationalRecordListView.vue';
import OperationalRecordDetailView from './features/operational-history/OperationalRecordDetailView.vue';
import ScheduledJobsView from './features/operational-history/ScheduledJobsView.vue';
import LicencesView from './features/licences/LicencesView.vue';

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: OperationalHomeView
    },
    {
      path: '/search-observations',
      name: 'search-observations',
      component: SearchObservationListView
    },
    {
      path: '/search-observations/:id',
      name: 'search-observation-detail',
      component: SearchObservationDetailView
    },
    { path: '/jobs', name: 'jobs', component: OperationalRecordListView, props: { kind: 'jobs' } },
    { path: '/jobs/:id', name: 'jobs-detail', component: OperationalRecordDetailView, props: { kind: 'jobs' } },
    { path: '/scheduled-jobs', name: 'scheduled-jobs', component: ScheduledJobsView },
    { path: '/errors', name: 'errors', component: OperationalRecordListView, props: { kind: 'errors' } },
    { path: '/errors/:id', name: 'errors-detail', component: OperationalRecordDetailView, props: { kind: 'errors' } },
    { path: '/tool-calls', name: 'tool-calls', component: OperationalRecordListView, props: { kind: 'tool-calls' } },
    { path: '/tool-calls/:id', name: 'tool-calls-detail', component: OperationalRecordDetailView, props: { kind: 'tool-calls' } },
    { path: '/licences', name: 'licences', component: LicencesView }
  ]
});
