<template>
  <div class="space-y-6">
    <section class="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <h1 class="text-2xl font-semibold">Mein Inventar</h1>
        <p class="mt-1 text-sm text-zinc-400">Steam-Session mit HttpOnly-Cookie.</p>
      </div>
      <div class="flex flex-wrap gap-2">
        <UButton :to="`${apiBase}/api/auth/steam/login`" external color="primary" icon="i-lucide-log-in">
          Steam Login
        </UButton>
        <UButton :disabled="!profile" :loading="refreshing" color="neutral" variant="soft" icon="i-lucide-refresh-cw" @click="refreshInventory">
          Inventar syncen
        </UButton>
      </div>
    </section>

    <UAlert
      v-if="showAuthAlert"
      color="warning"
      variant="soft"
      icon="i-lucide-lock"
      title="Nicht angemeldet"
      description="Melde dich mit Steam an, damit LootBase dein CS2-Inventar abrufen und bewerten kann."
    />

    <section v-if="profile" class="grid gap-4 md:grid-cols-3">
      <div class="rounded-md border border-zinc-800 bg-[#101821] p-4 md:col-span-2">
        <div class="flex items-center gap-4">
          <span class="flex size-14 items-center justify-center overflow-hidden rounded-md bg-zinc-800">
            <img v-if="profile.avatarUrl" :src="profile.avatarUrl" :alt="profile.personaName" class="size-full object-cover">
            <span v-else class="text-lg font-semibold">{{ getInitials(profile.personaName) }}</span>
          </span>
          <div class="min-w-0">
            <h2 class="truncate text-xl font-semibold">{{ profile.personaName }}</h2>
            <p class="truncate text-sm text-zinc-500">{{ profile.steamId64 }}</p>
          </div>
        </div>
      </div>

      <div class="rounded-md border border-zinc-800 bg-[#101821] p-4">
        <p class="text-xs font-medium uppercase text-zinc-500">Inventarwert</p>
        <p class="mt-2 text-2xl font-semibold text-emerald-300">
          {{ formatCurrency(profile.inventoryValue, profile.currency) }}
        </p>
        <p class="mt-1 text-xs text-zinc-500">{{ profile.itemCount }} Items</p>
      </div>
    </section>

    <section v-if="profile" class="space-y-3">
      <div>
        <h2 class="text-lg font-semibold">Top Items</h2>
        <p class="mt-1 text-sm text-zinc-500">Letzte Aktualisierung: {{ formatDateTime(profile.lastInventoryRefreshAt) }}</p>
      </div>
      <InventoryGrid :items="profile.topItems" />
    </section>
  </div>
</template>

<script setup lang="ts">
const apiBase = useApiBase()
const refreshing = ref(false)
const { data: profileResponse, error, refresh } = await useApiFetch<unknown>('/api/me')

const profile = computed(() => isPlayerProfile(profileResponse.value) ? profileResponse.value : null)
const showAuthAlert = computed(() => Boolean(error.value) || Boolean(profileResponse.value && !profile.value))

const refreshInventory = async () => {
  refreshing.value = true
  try {
    await $fetch('/api/me/inventory/refresh', {
      baseURL: apiBase,
      method: 'POST',
      credentials: 'include'
    })
    await refresh()
  } finally {
    refreshing.value = false
  }
}
</script>
