import { createRouter, createWebHistory } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'
import AdminSessionsView from '@/views/AdminSessionsView.vue'
import TerminalView from '@/views/TerminalView.vue'
import UnauthorizedView from '@/views/UnauthorizedView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/terminal',
    },
    {
      path: '/terminal',
      name: 'terminal',
      component: TerminalView,
    },
    {
      path: '/admin/sessions',
      name: 'admin-sessions',
      component: AdminSessionsView,
      meta: {
        requiresAuth: true,
      },
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: UnauthorizedView,
    },
  ],
})

router.beforeEach((to) => {
  if (to.meta.requiresAuth && !getBrowserAuthService().isAuthorized()) {
    return { name: 'unauthorized' }
  }

  return true
})

export default router
