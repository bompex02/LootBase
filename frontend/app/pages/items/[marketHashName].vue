<template>
  <div class="space-y-6">
    <UButton to="/" variant="ghost" color="neutral" icon="i-lucide-arrow-left">
      Zurueck
    </UButton>

    <UAlert
      v-if="pending"
      color="neutral"
      variant="soft"
      icon="i-lucide-loader"
      title="Lade Preisdaten ..."
    />

    <UAlert
      v-else-if="pricingError && !ownedItem"
      color="error"
      variant="soft"
      icon="i-lucide-circle-alert"
      title="Item nicht gefunden"
      :description="`Für '${marketHashName}' liegen aktuell keine Daten vor.`"
    />

    <template v-else-if="displayItem">
      <section class="rounded-md border border-zinc-800 bg-[#101821] p-5">
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="flex min-w-0 items-center gap-4">
            <span class="flex size-20 shrink-0 items-center justify-center overflow-hidden rounded-md bg-zinc-800">
              <img v-if="displayItem.iconUrl" :src="displayItem.iconUrl" :alt="displayItem.displayName" class="size-full object-contain">
              <UIcon v-else name="i-lucide-image-off" class="size-8 text-zinc-600" />
            </span>
            <div class="min-w-0">
              <h1 class="truncate text-2xl font-semibold">{{ displayItem.displayName }}</h1>
              <p class="mt-1 truncate text-sm text-zinc-500">{{ marketHashName }}</p>
              <div class="mt-3 flex flex-wrap gap-2">
                <span v-if="displayItem.type" class="rounded-md bg-zinc-800 px-2 py-1 text-xs text-zinc-300">
                  {{ displayItem.type }}
                </span>
                <span v-if="displayItem.exterior" class="rounded-md bg-zinc-800 px-2 py-1 text-xs text-zinc-300">
                  {{ displayItem.exterior }}
                </span>
                <span
                  v-if="displayItem.rarity"
                  class="rounded-md px-2 py-1 text-xs font-medium"
                  :style="{ backgroundColor: `${rarityColor}26`, color: rarityColor }"
                >
                  {{ displayItem.rarity }}
                </span>
              </div>
            </div>
          </div>

          <div v-if="ownedItem" class="shrink-0 rounded-md bg-emerald-500/10 px-4 py-3 text-right">
            <p class="text-xs font-medium uppercase text-emerald-200/70">Dein Bestand</p>
            <p class="mt-1 text-xl font-semibold text-emerald-300">
              {{ formatCurrency(ownedItem.totalPrice, ownedItem.currency) }}
            </p>
            <p class="mt-1 text-xs text-emerald-200/70">
              {{ ownedItem.quantity }}x &middot; {{ formatCurrency(ownedItem.unitPrice, ownedItem.currency) }} / Stück
            </p>
          </div>
        </div>
      </section>

      <section class="space-y-3">
        <UAlert
          v-if="pricingError"
          color="warning"
          variant="soft"
          icon="i-lucide-circle-alert"
          title="Keine Marktdaten"
          description="Für dieses Item liegen aktuell keine Preisdaten vor."
        />

        <template v-else-if="pricing">
          <div class="grid gap-2 text-xs text-zinc-500 sm:grid-cols-2">
            <UButton
              v-if="pricing.itemPage"
              :to="pricing.itemPage"
              external
              target="_blank"
              variant="link"
              color="neutral"
              icon="i-lucide-external-link"
              class="justify-start px-0"
            >
              Artikel-Seite
            </UButton>
            <UButton
              v-if="pricing.marketPage"
              :to="pricing.marketPage"
              external
              target="_blank"
              variant="link"
              color="neutral"
              icon="i-lucide-external-link"
              class="justify-start px-0"
            >
              Marktplatz
            </UButton>
          </div>
        </template>
      </section>

      <section class="space-y-3">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <h2 class="text-lg font-semibold">Preisverlauf</h2>
          <div class="flex gap-1 rounded-md border border-zinc-800 bg-[#101821] p-1">
            <button
              v-for="option in durationOptions"
              :key="option.key"
              type="button"
              class="cursor-pointer rounded px-2.5 py-1 text-xs font-medium transition-colors"
              :class="selectedDuration === option.key ? 'bg-zinc-700 text-zinc-100' : 'text-zinc-500 hover:text-zinc-300'"
              @click="selectedDuration = option.key"
            >
              {{ option.label }}
            </button>
          </div>
        </div>

        <UAlert
          v-if="historyError"
          color="warning"
          variant="soft"
          icon="i-lucide-circle-alert"
          title="Kein Preisverlauf"
          description="Für dieses Item liegen aktuell keine Verlaufsdaten vor."
        />

        <div v-else-if="history" class="rounded-md border border-zinc-800 bg-[#101821] p-5">
          <PriceHistoryChart
            :points="visibleHistoryPoints"
            :currency="history.currency"
            :color="rarityColor"
          />
        </div>
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import type { InventoryItem, PricingHistory, PricingHistoryPeriodKey, PricingItem } from '~/types/api'
import type { PriceHistoryChartPoint } from '~/components/PriceHistoryChart.vue'

const route = useRoute()
const marketHashName = computed(() => String(route.params.marketHashName))

const selectedItem = useState<InventoryItem | null>('selected-inventory-item')
const ownedItem = computed(() =>
  selectedItem.value?.marketHashName === marketHashName.value ? selectedItem.value : null)

const { data: pricing, pending, error: pricingError } = await useApiFetch<PricingItem>(
  `/api/pricing/items/${encodeURIComponent(marketHashName.value)}`,
  { query: { currency: ownedItem.value?.currency ?? 'EUR' } }
)

const { data: history, error: historyError } = await useApiFetch<PricingHistory>(
  `/api/pricing/history/${encodeURIComponent(marketHashName.value)}`,
  { query: { currency: ownedItem.value?.currency ?? 'EUR' } }
)

// Chart x-axis order: further back in time (left) to most recent (right)
const PERIODS_BY_DURATION: Record<PricingHistoryPeriodKey, PricingHistoryPeriodKey[]> = {
  '24h': ['24h'],
  '7d': ['7d', '24h'],
  '30d': ['30d', '7d', '24h'],
  '90d': ['90d', '30d', '7d', '24h']
}

const PERIOD_LABELS: Record<PricingHistoryPeriodKey, string> = {
  '24h': '24 Std.',
  '7d': '7 Tage',
  '30d': '30 Tage',
  '90d': '90 Tage'
}

const DAYS_BY_DURATION: Record<PricingHistoryPeriodKey, number> = {
  '24h': 1,
  '7d': 7,
  '30d': 30,
  '90d': 90
}

const durationOptions: Array<{ key: PricingHistoryPeriodKey, label: string }> = [
  { key: '7d', label: '7 Tage' },
  { key: '30d', label: '30 Tage' },
  { key: '90d', label: '90 Tage' }
]

const selectedDuration = ref<PricingHistoryPeriodKey>('90d')

const dailyPointFormatter = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' })

interface ChartPointSource {
  minPrice?: number | null
  maxPrice?: number | null
  avgPrice?: number | null
  medianPrice?: number | null
}

const toChartPoint = (key: string, label: string, source: ChartPointSource, volume: number): PriceHistoryChartPoint => ({
  key,
  label,
  min: source.minPrice ?? null,
  max: source.maxPrice ?? null,
  avg: source.avgPrice ?? null,
  median: source.medianPrice ?? null,
  volume
})

// Filter the history points to only include those within the selected duration window
const visibleHistoryPoints = computed<PriceHistoryChartPoint[]>(() => {
  if (!history.value) {
    return []
  }

  const days = DAYS_BY_DURATION[selectedDuration.value]
  const cutoff = Date.now() - days * 24 * 60 * 60 * 1000
  const dailyPointsInWindow = history.value.dailyPoints.filter(point => new Date(point.date).getTime() >= cutoff)

  if (dailyPointsInWindow.length >= 2) {
    return dailyPointsInWindow.map(point =>
      toChartPoint(point.date, dailyPointFormatter.format(new Date(point.date)), point, point.quantity))
  }

  return PERIODS_BY_DURATION[selectedDuration.value]
    .map(period => history.value!.periods.find(candidate => candidate.period === period))
    .filter((period): period is NonNullable<typeof period> => period !== undefined)
    .map(period => toChartPoint(period.period, PERIOD_LABELS[period.period], period, period.volume))
})

const displayItem = computed(() => {
  if (ownedItem.value) {
    return ownedItem.value
  }

  if (!pricing.value) {
    return null
  }

  return {
    displayName: marketHashName.value,
    iconUrl: route.query.icon ? String(route.query.icon) : undefined,
    type: route.query.type ? String(route.query.type) : undefined,
    exterior: route.query.exterior ? String(route.query.exterior) : undefined,
    rarity: route.query.rarity ? String(route.query.rarity) : undefined
  }
})

const rarityColor = computed(() => getRarityColor(displayItem.value?.rarity))
</script>
