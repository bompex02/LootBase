export default defineEventHandler((event) => {
  const config = useRuntimeConfig(event)
  return proxyRequest(event, `${config.apiBase}${event.path}`, {
    fetchOptions: { redirect: 'manual' }
  })
})
