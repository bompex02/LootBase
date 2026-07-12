interface ApiErrorMessage {
  title: string
  description: string
  color: 'error' | 'warning'
}

// Known, backend-curated error codes only. We never render arbitrary/raw
// backend error text to the user - only these pre-approved messages, so a
// stray exception message or upstream response can never leak through a toast.
const KNOWN_ERROR_CODES: Record<string, ApiErrorMessage> = {
  steam_rate_limited: {
    title: 'Steam-Rate-Limit erreicht',
    description: 'Steam hat gerade zu viele Anfragen erhalten. Bitte warte ein bis zwei Minuten und versuche es erneut.',
    color: 'warning'
  },
  steam_inventory_private: {
    title: 'Inventar ist privat',
    description: 'Dein Steam-Inventar ist nicht öffentlich einsehbar. Stelle die Privatsphäre-Einstellung in deinem Steam-Profil auf "Öffentlich" und versuche es erneut.',
    color: 'warning'
  },
  steam_inventory_error: {
    title: 'Steam-Inventar konnte nicht geladen werden',
    description: 'Steam hat die Anfrage gerade abgelehnt. Bitte versuche es in ein paar Minuten erneut.',
    color: 'error'
  }
}

const extractStatusCode = (error: unknown): number | null => {
  if (!error || typeof error !== 'object') {
    return null
  }

  const candidate = error as { statusCode?: number, status?: number }
  return candidate.statusCode ?? candidate.status ?? null
}

const extractErrorCode = (error: unknown): string | null => {
  if (!error || typeof error !== 'object') {
    return null
  }

  const data = (error as { data?: unknown }).data
  if (!data || typeof data !== 'object') {
    return null
  }

  const code = (data as { code?: unknown }).code
  return typeof code === 'string' ? code : null
}

// Resolves an API error to a user-friendly message, or null if the error is a 404 (not found)
export const resolveApiError = (error: unknown): ApiErrorMessage | null => {
  const statusCode = extractStatusCode(error)

  if (statusCode === 404) {
    return null
  }

  const code = extractErrorCode(error)
  if (code && KNOWN_ERROR_CODES[code]) {
    return KNOWN_ERROR_CODES[code]
  }

  switch (statusCode) {
    case 401:
      return {
        title: 'Nicht angemeldet',
        description: 'Bitte melde dich mit Steam an, um fortzufahren.',
        color: 'warning'
      }
    case 403:
      return {
        title: 'Keine Berechtigung',
        description: 'Du hast keine Berechtigung, diese Aktion auszuführen.',
        color: 'warning'
      }
    case 400:
      return {
        title: 'Ungültige Anfrage',
        description: 'Deine Anfrage konnte nicht verarbeitet werden. Bitte lade die Seite neu und versuche es erneut.',
        color: 'error'
      }
    case 429:
      return {
        title: 'Zu viele Anfragen',
        description: 'Bitte warte einen Moment und versuche es erneut.',
        color: 'warning'
      }
  }

  if (statusCode && statusCode >= 500) {
    return {
      title: 'Serverfehler',
      description: 'Es ist ein unerwarteter Fehler aufgetreten. Bitte versuche es später erneut.',
      color: 'error'
    }
  }

  return {
    title: 'Verbindungsfehler',
    description: 'Der Server ist gerade nicht erreichbar. Bitte überprüfe deine Internetverbindung und versuche es erneut.',
    color: 'warning'
  }
}

export const notifyApiError = (error: unknown) => {
  if (extractStatusCode(error) === 401) {
    // The server no longer considers us logged in (e.g. an expired or
    // invalidated session) - drop the client-side "logged in" cookie too so
    // the UI (header avatar, "own profile" checks) doesn't keep claiming
    // we're authenticated when the backend disagrees.
    useCurrentSteamId().value = null
  }

  const resolved = resolveApiError(error)
  if (!resolved) {
    return
  }

  useToast().add({
    title: resolved.title,
    description: resolved.description,
    color: resolved.color,
    icon: resolved.color === 'warning' ? 'i-lucide-alert-triangle' : 'i-lucide-circle-alert'
  })
}
