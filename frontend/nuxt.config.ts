export default defineNuxtConfig({
  compatibilityDate: '2026-07-03',
  devtools: { enabled: true },
  modules: ['@nuxt/ui'],
  css: ['~/assets/css/main.css'],
  routeRules: {
    '/api/**': {
      proxy: `${process.env.NUXT_API_BASE ?? 'http://localhost:5188'}/api/**`
    }
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
