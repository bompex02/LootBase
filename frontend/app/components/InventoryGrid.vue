<template>
  <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
    <article
      v-for="item in items"
      :key="item.assetId"
      class="rounded-md border border-zinc-800 bg-[#101821] p-4"
    >
      <div class="flex items-start justify-between gap-4">
        <div class="min-w-0">
          <h3 class="truncate text-sm font-semibold">{{ item.displayName }}</h3>
          <p class="mt-1 truncate text-xs text-zinc-500">{{ item.marketHashName }}</p>
        </div>
        <span class="rounded-md bg-emerald-500/10 px-2 py-1 text-xs font-semibold text-emerald-300">
          {{ formatCurrency(item.totalPrice, item.currency) }}
        </span>
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
</script>
