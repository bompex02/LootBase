<template>
  <div class="space-y-2">
    <div v-for="stat in stats" :key="stat.label" class="flex items-center gap-3 text-xs">
      <span class="w-20 shrink-0 text-zinc-500">{{ stat.label }}</span>
      <div class="relative h-6 flex-1 overflow-hidden rounded bg-zinc-800/60">
        <div
          class="h-full rounded transition-[width]"
          :style="{ width: `${barWidth(stat.value)}%`, backgroundColor: color }"
        />
        <span class="absolute inset-0 flex items-center px-2 text-[11px] font-medium text-zinc-100">
          {{ formatCurrency(stat.value, currency) }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
const props = defineProps<{
  currency: string
  color: string
  stats: Array<{ label: string, value: number }>
}>()

const maxValue = computed(() => Math.max(...props.stats.map(stat => stat.value), 0))

const barWidth = (value: number) => maxValue.value === 0 ? 0 : Math.max((value / maxValue.value) * 100, 4)
</script>
