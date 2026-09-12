<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between gap-3">
      <UButton to="/" variant="ghost" color="neutral" icon="i-lucide-arrow-left">
        Zurueck
      </UButton>
      <UButton v-if="isOwnProfile" class="cursor-pointer" :disabled="!profile" :loading="refreshing" color="neutral" variant="soft" icon="i-lucide-refresh-cw" @click="refreshInventory">
        Inventar syncen
      </UButton>
    </div>

    <UAlert
      v-if="error"
      color="error"
      variant="soft"
      icon="i-lucide-circle-alert"
      title="Spieler nicht gefunden"
      description="Dieser Steam-Account ist noch nicht im Leaderboard."
    />

    <template v-if="profile">
      <section class="grid gap-4 md:grid-cols-[1fr_280px]">
        <div class="rounded-md border border-zinc-800 bg-[#101821] p-5">
          <div class="flex items-center gap-4">
            <span class="flex size-16 items-center justify-center overflow-hidden rounded-md bg-zinc-800">
              <img v-if="profile.avatarUrl" :src="profile.avatarUrl" :alt="profile.personaName" class="size-full object-cover">
              <span v-else class="text-xl font-semibold">{{ getInitials(profile.personaName) }}</span>
            </span>
            <div class="min-w-0">
              <h1 class="truncate text-2xl font-semibold">{{ profile.personaName }}</h1>
              <p class="truncate text-sm text-zinc-500">{{ profile.steamId64 }}</p>
            </div>
          </div>
        </div>

        <div class="rounded-md border border-zinc-800 bg-[#101821] p-5">
          <p class="text-xs font-medium uppercase text-zinc-500">Gesamtwert</p>
          <p class="mt-2 text-2xl font-semibold text-emerald-300">
            {{ formatCurrency(profile.inventoryValue, profile.currency) }}
          </p>
          <p class="mt-1 text-xs text-zinc-500">{{ profile.itemCount }} Items</p>
        </div>
      </section>

      <section class="space-y-3">
        <div>
          <h2 class="text-lg font-semibold">Top 20 Items</h2>
          <p class="mt-1 text-sm text-zinc-500">Letzte Aktualisierung: {{ formatDateTime(profile.lastInventoryRefreshAt) }}</p>
        </div>
        <InventoryGrid :items="profile.topItems" />
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
const route = useRoute()
const steamId64 = computed(() => String(route.params.steamId64))
const currentSteamId = useCurrentSteamId()
const isOwnProfile = computed(() => !!currentSteamId.value && currentSteamId.value === steamId64.value)
const refreshing = ref(false)

const { data: profileResponse, error, refresh } = await useApiFetch<unknown>(
  `/api/players/${steamId64.value}`
)

const profile = computed(() => isPlayerProfile(profileResponse.value) ? profileResponse.value : null)

const refreshInventory = async () => {
  refreshing.value = true
  try {
    await $fetch(`/api/players/${steamId64.value}/inventory/refresh`, {
      method: 'POST',
      credentials: 'include'
    })
    await refresh()
  } catch (err) {
    notifyApiError(err)
  } finally {
    refreshing.value = false
  }
}
</script>
