<template>
  <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
    <InventoryItemCard
      v-for="item in items"
      :key="item.assetId"
      :item="item"
      :is-total-price-visible="isTotalPriceVisible(item)"
      @toggle-price-mode="togglePriceMode"
      @select="handleSelect"
    />
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
