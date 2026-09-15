<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold">Pricing Pipeline</h1>
      <UButton :loading="pending" variant="soft" color="neutral" icon="i-lucide-refresh-cw" @click="refresh()">
        Aktualisieren
      </UButton>
    </div>

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      icon="i-lucide-circle-alert"
      title="Status nicht erreichbar"
      description="Backend antwortet gerade nicht."
    />

    <template v-else-if="status">
      <section class="grid gap-4 sm:grid-cols-3">
        <div class="rounded-md border border-zinc-800 bg-[#101821] p-5">
          <p class="text-xs font-medium uppercase text-zinc-500">Steam-Backfill abgedeckt</p>
          <p class="mt-2 text-2xl font-semibold text-emerald-300">
            {{ status.coveredItems.toLocaleString('de-DE') }} / {{ status.totalItems.toLocaleString('de-DE') }}
          </p>
          <p class="mt-1 text-xs text-zinc-500">{{ percentCovered }}% fertig</p>
        </div>

        <div class="rounded-md border border-zinc-800 bg-[#101821] p-5">
          <p class="text-xs font-medium uppercase text-zinc-500">Noch offen</p>
          <p class="mt-2 text-2xl font-semibold">
            {{ status.remainingItems.toLocaleString('de-DE') }}
          </p>
        </div>

        <div class="rounded-md border border-zinc-800 bg-[#101821] p-5">
          <p class="text-xs font-medium uppercase text-zinc-500">Letzter Tages-Snapshot</p>
          <p class="mt-2 text-2xl font-semibold" :class="snapshotIsStale ? 'text-amber-400' : 'text-emerald-300'">
            {{ status.lastDailySnapshotDate ?? 'Nie' }}
          </p>
          <p v-if="snapshotIsStale" class="mt-1 text-xs text-amber-400">Älter als 2 Tage</p>
        </div>
      </section>

      <div class="h-2 overflow-hidden rounded-full bg-zinc-800">
        <div class="h-full rounded-full bg-emerald-500 transition-all" :style="{ width: `${percentCovered}%` }" />
      </div>

      <p class="text-xs text-zinc-500">
        Aktualisiert automatisch alle 30s &middot; zuletzt geladen: {{ lastLoadedAt }}
      </p>
    </template>
  </div>
</template>

<script setup lang="ts">
import type { PricingPipelineStatus } from '~/types/api'

const { data: status, error, pending, refresh } = await useApiFetch<PricingPipelineStatus>(
  '/api/pricing/status',
  { toastOnError: false }
)

const lastLoadedAt = ref('')
const setLoadedNow = () => {
  lastLoadedAt.value = new Intl.DateTimeFormat('de-DE', { timeStyle: 'medium' }).format(new Date())
}

const percentCovered = computed(() => {
  if (!status.value || status.value.totalItems === 0) {
    return 0
  }
  return Math.round((status.value.coveredItems / status.value.totalItems) * 100)
})

const snapshotIsStale = computed(() => {
  if (!status.value?.lastDailySnapshotDate) {
    return true
  }
  const ageMs = Date.now() - new Date(status.value.lastDailySnapshotDate).getTime()
  return ageMs > 2 * 24 * 60 * 60 * 1000
})

let timer: ReturnType<typeof setInterval> | undefined

onMounted(() => {
  setLoadedNow()
  timer = setInterval(async () => {
    await refresh()
    setLoadedNow()
  }, 30_000)
})

onUnmounted(() => {
  clearInterval(timer)
})
</script>
