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
        <h2 class="text-lg font-semibold">Marktpreise</h2>

        <UAlert
          v-if="pricingError"
          color="warning"
          variant="soft"
          icon="i-lucide-circle-alert"
          title="Keine Marktdaten"
          description="Für dieses Item liegen aktuell keine Preisdaten vor."
        />

        <template v-else-if="pricing">
          <div class="rounded-md border border-zinc-800 bg-[#101821] p-5">
            <PriceStatsChart
              v-if="priceStats.length"
              :stats="priceStats"
              :currency="pricing.currency"
              :color="rarityColor"
            />
            <p v-else class="text-sm text-zinc-500">Keine Preisstatistik verfügbar.</p>
          </div>

          <div class="grid gap-2 text-xs text-zinc-500 sm:grid-cols-2">
            <p>Quelle: <span class="text-zinc-300">{{ pricing.source }}</span></p>
            <p>Aktualisiert: <span class="text-zinc-300">{{ formatDateTime(pricing.updatedAt ?? pricing.retrievedAt) }}</span></p>
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
    </template>
  </div>
</template>

<script setup lang="ts">
import type { InventoryItem, PricingItem } from '~/types/api'

const route = useRoute()
const marketHashName = computed(() => String(route.params.marketHashName))

const selectedItem = useState<InventoryItem | null>('selected-inventory-item')
const ownedItem = computed(() =>
  selectedItem.value?.marketHashName === marketHashName.value ? selectedItem.value : null)

const { data: pricing, pending, error: pricingError } = await useApiFetch<PricingItem>(
  `/api/pricing/items/${encodeURIComponent(marketHashName.value)}`,
  { query: { currency: ownedItem.value?.currency ?? 'EUR' } }
)

const displayItem = computed(() => {
  if (ownedItem.value) {
    return ownedItem.value
  }

  if (!pricing.value) {
    return null
  }

  return {
    displayName: marketHashName.value,
    iconUrl: null as string | null,
    type: null as string | null,
    exterior: null as string | null,
    rarity: null as string | null
  }
})

const rarityColor = computed(() => getRarityColor(displayItem.value?.rarity))

const priceStats = computed(() => {
  if (!pricing.value) {
    return []
  }

  return [
    { label: 'Min', value: pricing.value.minPrice },
    { label: 'Median', value: pricing.value.medianPrice },
    { label: 'Mittelwert', value: pricing.value.meanPrice },
    { label: 'Empfohlen', value: pricing.value.suggestedPrice },
    { label: 'Max', value: pricing.value.maxPrice }
  ].filter((stat): stat is { label: string, value: number } => typeof stat.value === 'number')
})
</script>
