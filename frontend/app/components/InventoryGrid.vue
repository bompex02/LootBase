<template>
  <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
    <article
      v-for="item in items"
      :key="item.assetId"
      class="rounded-md border border-zinc-800 bg-[#101821] p-4"
    >
      <div class="flex items-start justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <span class="flex size-14 shrink-0 items-center justify-center overflow-hidden rounded-md bg-zinc-800">
            <img v-if="item.iconUrl" :src="item.iconUrl" :alt="item.displayName" class="size-full object-contain">
            <UIcon v-else name="i-lucide-image-off" class="size-6 text-zinc-600" />
          </span>
          <div class="min-w-0">
            <h3 class="truncate text-sm font-semibold">{{ item.displayName }}</h3>
            <p class="mt-1 truncate text-xs text-zinc-500">{{ item.marketHashName }}</p>
          </div>
        </div>
        <button
          type="button"
          class="shrink-0 rounded-md bg-emerald-500/10 px-2 py-1 text-right text-xs font-semibold text-emerald-300 transition hover:bg-emerald-500/15 focus:outline-none focus:ring-2 focus:ring-emerald-400/50"
          :title="isTotalPriceVisible(item) ? 'Stückpreis anzeigen' : 'Gesamtpreis anzeigen'"
          @click="togglePriceMode(item)"
        >
          <span class="block leading-none">
            {{ formatCurrency(displayedPrice(item), item.currency) }}
          </span>
          <span v-if="item.quantity > 1" class="mt-1 block text-[10px] font-medium text-emerald-200/70">
            {{ isTotalPriceVisible(item) ? 'gesamt' : 'Stück' }}
          </span>
        </button>
      </div>

      <dl class="mt-4 grid grid-cols-2 gap-3 text-xs">
        <div>
          <dt class="text-zinc-500">Typ</dt>
          <dd class="mt-1 text-zinc-200">{{ item.type ?? '-' }}</dd>
        </div>
        <div>
          <dt class="text-zinc-500">Wear</dt>
          <dd class="mt-1 text-zinc-200">{{ item.exterior ?? '-' }}</dd>
        </div>
        <div>
          <dt class="text-zinc-500">Seltenheit</dt>
          <dd class="mt-1 text-zinc-200">{{ item.rarity ?? '-' }}</dd>
        </div>
        <div>
          <dt class="text-zinc-500">Anzahl</dt>
          <dd class="mt-1 text-zinc-200">{{ item.quantity }}</dd>
        </div>
      </dl>
    </article>
  </div>
</template>

<script setup lang="ts">
import type { InventoryItem } from '~/types/api'

defineProps<{
  items: InventoryItem[]
}>()

const totalPriceItemKeys = ref<Set<string>>(new Set())

const getItemKey = (item: InventoryItem) => item.marketHashName || item.assetId

const isTotalPriceVisible = (item: InventoryItem) => totalPriceItemKeys.value.has(getItemKey(item))

const displayedPrice = (item: InventoryItem) =>
  isTotalPriceVisible(item) ? item.totalPrice : item.unitPrice

const togglePriceMode = (item: InventoryItem) => {
  const key = getItemKey(item)
  const nextKeys = new Set(totalPriceItemKeys.value)

  if (nextKeys.has(key)) {
    nextKeys.delete(key)
  } else {
    nextKeys.add(key)
  }

  totalPriceItemKeys.value = nextKeys
}
</script>
