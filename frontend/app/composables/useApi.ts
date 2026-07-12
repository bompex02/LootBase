export const useCurrentSteamId = () => useCookie<string | null>('lootbase.steamId64', { default: () => null })

export const useApiFetch = <T>(path: string, options: { toastOnError?: boolean, [key: string]: unknown } = {}) => {
  const { toastOnError = true, ...fetchOptions } = options
  const headers = import.meta.server ? useRequestHeaders(['cookie']) : undefined

  const result = useFetch<T>(path, {
    credentials: 'include',
    headers,
    ...fetchOptions
  })

  if (toastOnError && import.meta.client) {
    watch(result.error, (value) => {
      if (value) {
        notifyApiError(value)
      }
    })
  }

  return result
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

const RARITY_COLORS: Record<string, string> = {
  'consumer grade': '#b0c3d9',
  'industrial grade': '#5e98d9',
  'mil-spec grade': '#4b69ff',
  'restricted': '#8847ff',
  'classified': '#d32ce6',
  'covert': '#eb4b4b',
  'contraband': '#e4ae39',
  'extraordinary': '#e4ae39'
}

export const getRarityColor = (rarity?: string | null) => {
  if (!rarity) {
    return '#71717a'
  }

  return RARITY_COLORS[rarity.trim().toLowerCase()] ?? '#71717a'
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
