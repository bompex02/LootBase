<template>
  <div class="overflow-hidden rounded-md border border-zinc-800 bg-[#101821]">
    <div class="grid grid-cols-[64px_1fr_140px_120px] border-b border-zinc-800 px-4 py-3 text-xs font-medium uppercase text-zinc-500 max-md:hidden">
      <span>Rang</span>
      <span>Spieler</span>
      <span class="text-right">Inventar</span>
      <span class="text-right">Items</span>
    </div>

    <div v-if="entries.length === 0" class="px-4 py-10 text-center text-sm text-zinc-400">
      Keine Spieler gefunden.
    </div>

    <NuxtLink
      v-for="entry in entries"
      :key="entry.steamId64"
      :to="`/players/${entry.steamId64}`"
      class="grid grid-cols-[64px_1fr_140px_120px] items-center border-b border-zinc-800 px-4 py-4 last:border-b-0 hover:bg-zinc-800/60 max-md:grid-cols-[48px_1fr] max-md:gap-y-2"
    >
      <span class="text-sm font-semibold text-zinc-400">#{{ entry.rank }}</span>

      <span class="flex min-w-0 items-center gap-3">
        <span class="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-zinc-800">
          <img
            v-if="entry.avatarUrl"
            :src="entry.avatarUrl"
            :alt="entry.personaName"
            class="size-full object-cover"
          >
          <span v-else class="text-sm font-semibold text-zinc-300">{{ getInitials(entry.personaName) }}</span>
        </span>
        <span class="min-w-0">
          <span class="block truncate text-sm font-medium">{{ entry.personaName }}</span>
          <span class="block truncate text-xs text-zinc-500">{{ entry.steamId64 }}</span>
        </span>
      </span>

      <span class="text-right text-sm font-semibold text-emerald-300 max-md:col-start-2 max-md:text-left">
        {{ formatCurrency(entry.inventoryValue, entry.currency) }}
      </span>
      <span class="text-right text-sm text-zinc-400 max-md:col-start-2 max-md:text-left">
        {{ entry.itemCount }}
      </span>
    </NuxtLink>
  </div>
</template>

<script setup lang="ts">
import type { LeaderboardEntry } from '~/types/api'

defineProps<{
  entries: LeaderboardEntry[]
}>()
</script>
