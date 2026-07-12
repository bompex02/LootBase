export default defineNuxtConfig({
  compatibilityDate: '2026-07-03',
  devtools: { enabled: true },
  modules: ['@nuxt/ui'],
  css: ['~/assets/css/main.css'],
<<<<<<< HEAD
  routeRules: {
    '/api/**': {
      proxy: `${process.env.NUXT_API_BASE ?? 'http://localhost:5188'}/api/**`
    }
=======
  runtimeConfig: {
    // Server-only: the real backend address, used by server/routes/api/[...].ts to proxy all API calls
    apiBase: process.env.NUXT_API_BASE ?? 'http://localhost:5188'
>>>>>>> be41e55bd6fad63f092c84ee8f913b318767697c
  },
  ui: {
    fonts: false,
    theme: {
      colors: ['primary', 'neutral', 'success', 'warning', 'error']
    }
  },
  icon: {
    localApiEndpoint: '/_nuxt_icon',
    serverBundle: {
      collections: ['lucide']
    }
  },
  typescript: {
    typeCheck: true,
    strict: true
  },
  vite: {
    optimizeDeps: {
      include: [
        '@vue/devtools-core',
        '@vue/devtools-kit',
      ]
    }
  }
})
