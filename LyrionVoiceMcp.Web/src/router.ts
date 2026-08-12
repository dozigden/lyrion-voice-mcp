import { createRouter, createWebHistory } from 'vue-router';
import OperationalHomeView from './features/operations/OperationalHomeView.vue';
import SearchObservationDetailView from './features/search-observations/SearchObservationDetailView.vue';
import SearchObservationListView from './features/search-observations/SearchObservationListView.vue';

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
    }
  ]
});
