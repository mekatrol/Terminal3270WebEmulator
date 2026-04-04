import { createRouter, createWebHistory } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'
import AdminSessionsView from '@/views/AdminSessionsView.vue'
import AuthCallbackView from '@/views/AuthCallbackView.vue'
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
      meta: {
        requiresAuth: true,
      },
    },
    {
      path: '/admin/sessions',
      name: 'admin-sessions',
      component: AdminSessionsView,
      meta: {
        requiresAuth: true,
        requiredRole: 'Server.Admin',
      },
    },
    {
      path: '/auth/callback',
      name: 'auth-callback',
      component: AuthCallbackView,
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: UnauthorizedView,
    },
  ],
})

router.beforeEach(async (to) => {
  const authService = getBrowserAuthService()

  if (!to.meta.requiresAuth) {
    return true
  }

  const hasSession = await authService.ensureSession()

  if (!hasSession) {
    await authService.beginSignIn(to.fullPath)
    return false
  }

  const requiredRole =
    typeof to.meta.requiredRole === 'string' ? to.meta.requiredRole : undefined

  if (!authService.isAuthorized(requiredRole)) {
    return { name: 'unauthorized' }
  }

  return true
})

export default router
