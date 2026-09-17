<!-- pages/index.vue -->
<script setup lang="ts">
// Hapus import { Button } manual karena Nuxt 3 / shadcn otomatis meng-import komponen.
// Jika di proyekmu tidak auto-import, gunakan pemanggilan nama komponen sesuai konvensi.

const handleEnter = () => {
  navigateTo('/home')
}
</script>

<template>
  <div class="relative w-full min-h-screen">
    <!-- Nuxt Welcome Screen Bawaan -->
    <NuxtWelcome />

    <!-- Floating Action Button di kanan atas -->
    <div class="fixed top-6 right-6 z-[99999]">
      <UiButton 
        size="lg" 
        class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold px-6 shadow-xl transition-all hover:scale-105 gap-2 cursor-pointer relative pointer-events-auto"
        @click="handleEnter"
      >
        <span>Masuk ke Dalam Web</span>
        <!-- Gunakan ClientOnly agar komponen Icon tidak crash saat SSR -->
        <ClientOnly>
          <Icon name="tabler:arrow-right" class="w-5 h-5" />
        </ClientOnly>
      </UiButton>
    </div>
  </div>
</template>
