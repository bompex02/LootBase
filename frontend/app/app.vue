<template>
  <UApp>
    <div class="min-h-screen bg-[#0b0f14] text-zinc-100">
      <header class="border-b border-zinc-800 bg-[#0f151c]">
        <div class="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
          <NuxtLink to="/" class="flex items-center gap-3">
            <span class="flex size-9 items-center justify-center rounded-md bg-emerald-500 text-sm font-black text-zinc-950">LB</span>
            <span>
              <span @click="navigateTo('/')" class="block text-base font-semibold">LootBase</span>
              <span class="block text-xs text-zinc-400">CS2 inventory leaderboard</span>
            </span>
          </NuxtLink>

          <nav class="flex flex-wrap items-center gap-2">
            <UButton to="/" variant="ghost" color="neutral" icon="i-lucide-trophy">
              Leaderboard
            </UButton>

            <UDropdownMenu v-if="profile" :items="accountMenuItems">
              <button type="button" class="flex cursor-pointer items-center gap-2 rounded-md border border-zinc-800 bg-[#101821] py-1 pl-1 pr-3">
                <span class="flex size-7 items-center justify-center overflow-hidden rounded-full bg-zinc-800">
                  <img v-if="profile.avatarUrl" :src="profile.avatarUrl" :alt="profile.personaName" class="size-full object-cover">
                  <span v-else class="text-xs font-semibold">{{ getInitials(profile.personaName) }}</span>
                </span>
                <span class="max-w-32 truncate text-sm font-medium">{{ profile.personaName }}</span>
              </button>
            </UDropdownMenu>
            <UButton v-else to="/api/auth/steam/login" external color="primary" icon="i-lucide-log-in">
              Mit Steam anmelden
            </UButton>
          </nav>
        </div>
      </header>

      <main class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
        <NuxtPage />
      </main>
    </div>
  </UApp>
</template>

<script setup lang="ts">
const currentSteamId = useCurrentSteamId()
const { data: profileResponse } = currentSteamId.value
  ? await useApiFetch<unknown>(`/api/players/${currentSteamId.value}`)
  : { data: ref(null) }
const profile = computed(() => isPlayerProfile(profileResponse.value) ? profileResponse.value : null)

const logout = async () => {
  await $fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
  currentSteamId.value = null
  await navigateTo('/', { external: true })
}

const accountMenuItems = computed(() => [[
  {
    label: 'Profil ansehen',
    icon: 'i-lucide-user',
    to: profile.value ? `/players/${profile.value.steamId64}` : undefined
  },
  {
    label: 'Ausloggen',
    icon: 'i-lucide-log-out',
    onSelect: logout
  }
]])
</script>
