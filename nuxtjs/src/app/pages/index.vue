<!-- pages/index.vue -->
<script setup lang="ts">
import { Button } from '@/components/ui/button'

const handleEnter = () => {
  navigateTo('/home')
}
</script>

<template>
  <div class="relative min-h-screen">
    <!-- Menyuntikkan tombol ke dalam slot default NuxtWelcome -->
    <NuxtWelcome>
      <div class="fixed top-6 right-6 z-[999999] pointer-events-auto">
        <Button 
          size="lg" 
          class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold px-6 shadow-xl transition-all hover:scale-105 gap-2 cursor-pointer relative"
          @click="handleEnter"
        >
          <span>Masuk ke Dalam Web</span>
          <ClientOnly>
            <Icon name="tabler:arrow-right" class="w-5 h-5" />
          </ClientOnly>
        </Button>
      </div>
    </NuxtWelcome>
  </div>
</template>
