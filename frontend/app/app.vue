<template>
  <UApp>
    <div class="min-h-screen bg-[#0b0f14] text-zinc-100">
      <header class="border-b border-zinc-800 bg-[#0f151c]">
        <div class="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
          <NuxtLink to="/" class="flex items-center gap-3">
            <span class="flex size-9 items-center justify-center rounded-md bg-emerald-500 text-sm font-black text-zinc-950">LB</span>
            <span>
              <span class="block text-base font-semibold">LootBase</span>
              <span class="block text-xs text-zinc-400">CS2 inventory leaderboard</span>
            </span>
          </NuxtLink>

          <nav class="flex flex-wrap items-center gap-2">
            <UButton to="/" variant="ghost" color="neutral" icon="i-lucide-trophy">
              Leaderboard
            </UButton>
            <UButton to="/me" variant="ghost" color="neutral" icon="i-lucide-user">
              Mein Profil
            </UButton>
            <UButton :to="`${apiBase}/api/auth/steam/login`" external color="primary" icon="i-lucide-log-in">
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
const apiBase = useApiBase()
</script>
