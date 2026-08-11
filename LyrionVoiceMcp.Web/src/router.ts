import { createRouter, createWebHistory } from 'vue-router';
import OperationalHomeView from './features/operations/OperationalHomeView.vue';

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: OperationalHomeView
    }
  ]
});

