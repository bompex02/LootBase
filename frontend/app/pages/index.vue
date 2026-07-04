<template>
  <div class="space-y-6">
    <section class="grid gap-4 md:grid-cols-3">
      <div class="rounded-md border border-zinc-800 bg-[#101821] p-4">
        <p class="text-xs font-medium uppercase text-zinc-500">Spieler</p>
        <p class="mt-2 text-2xl font-semibold">{{ leaderboard?.length ?? 0 }}</p>
      </div>
      <div class="rounded-md border border-zinc-800 bg-[#101821] p-4">
        <p class="text-xs font-medium uppercase text-zinc-500">Top-Inventar</p>
        <p class="mt-2 text-2xl font-semibold text-emerald-300">
          {{ formatCurrency(topValue, topCurrency) }}
        </p>
      </div>
      <div class="rounded-md border border-zinc-800 bg-[#101821] p-4">
        <p class="text-xs font-medium uppercase text-zinc-500">Spiel</p>
        <p class="mt-2 text-2xl font-semibold">Counter-Strike 2</p>
      </div>
    </section>

    <section class="space-y-3">
      <div class="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 class="text-2xl font-semibold">Leaderboard</h1>
          <p class="mt-1 text-sm text-zinc-400">Inventarwert nach CS2-Marktwert.</p>
        </div>
        <UButton :loading="pending" color="neutral" variant="soft" icon="i-lucide-refresh-cw" @click="refresh()">
          Aktualisieren
        </UButton>
      </div>

      <UAlert
        v-if="error"
        color="error"
        variant="soft"
        icon="i-lucide-circle-alert"
        title="API nicht erreichbar"
        :description="error.message"
      />

      <LeaderboardTable :entries="leaderboard ?? []" />
    </section>
  </div>
</template>

<script setup lang="ts">
import type { LeaderboardEntry } from '~/types/api'

const { data: leaderboard, pending, error, refresh } = await useApiFetch<LeaderboardEntry[]>('/api/leaderboard')

const topValue = computed(() => leaderboard.value?.[0]?.inventoryValue ?? 0)
const topCurrency = computed(() => leaderboard.value?.[0]?.currency ?? 'EUR')
</script>
