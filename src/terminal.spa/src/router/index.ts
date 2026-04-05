import { createRouter, createWebHistory } from 'vue-router'
import type { RouteLocationNormalized } from 'vue-router'

import { getBrowserAuthService } from '@/services/auth'
import AdminSessionsView from '@/views/AdminSessionsView.vue'
import AuthCallbackView from '@/views/AuthCallbackView.vue'
import SignedOutView from '@/views/SignedOutView.vue'
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
      path: '/signed-out',
      name: 'signed-out',
      component: SignedOutView,
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: UnauthorizedView,
    },
  ],
})

function resolvePostLogoutReturnPath(to: RouteLocationNormalized): string | null {
  if (to.name === 'auth-callback') {
    return null
  }

  if (typeof to.query.state !== 'string' || !to.query.state.startsWith('/')) {
    return null
  }

  return to.query.state
}

router.beforeEach(async (to) => {
  const postLogoutReturnPath = resolvePostLogoutReturnPath(to)

  if (postLogoutReturnPath) {
    return postLogoutReturnPath
  }

  const authService = getBrowserAuthService()

  if (!to.meta.requiresAuth) {
    return true
  }

  const hasSession = await authService.ensureSession()

  if (!hasSession) {
    await authService.beginSignIn(to.fullPath)
    return false
  }

  const requiredRole = typeof to.meta.requiredRole === 'string' ? to.meta.requiredRole : undefined

  if (!authService.isAuthorized(requiredRole)) {
    return {
      name: 'unauthorized',
      query: {
        returnTo: to.fullPath,
      },
    }
  }

  return true
})

export default router
