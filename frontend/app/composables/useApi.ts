export const useApiBase = () => {
  const config = useRuntimeConfig()
  return config.public.apiBase
}

export const useApiFetch = <T>(path: string, options = {}) => {
  const apiBase = useApiBase()

  return useFetch<T>(path, {
    baseURL: apiBase,
    credentials: 'include',
    ...options
  })
}

export const formatCurrency = (value: number, currency = 'EUR') => {
  return new Intl.NumberFormat('de-DE', {
    style: 'currency',
    currency,
    maximumFractionDigits: 2
  }).format(value)
}

export const getInitials = (name?: string | null) => {
  if (!name) {
    return '??'
  }

  return name.slice(0, 2).toUpperCase()
}

export const formatDateTime = (value?: string | null) => {
  if (!value) {
    return 'Noch nicht aktualisiert'
  }

  return new Intl.DateTimeFormat('de-DE', {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(new Date(value))
}

export const isPlayerProfile = (value: unknown): value is import('~/types/api').PlayerProfile => {
  if (!value || typeof value !== 'object') {
    return false
  }

  const candidate = value as Record<string, unknown>
  return typeof candidate.steamId64 === 'string' &&
    typeof candidate.personaName === 'string' &&
    typeof candidate.inventoryValue === 'number' &&
    typeof candidate.currency === 'string' &&
    typeof candidate.itemCount === 'number' &&
    Array.isArray(candidate.topItems)
}
