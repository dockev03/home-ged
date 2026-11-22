<script setup lang="ts">
import { watchEffect } from 'vue'
import { useMediaQuery } from './composables/mediaQuery'
import { applyBrowserTheme } from './format/theme'
import { usePreferencesStore } from './stores/preferences'

// Thème choisi sur cet appareil (PWA-03) ; par défaut, celui du système.
const preferences = usePreferencesStore()
const systemDark = useMediaQuery('(prefers-color-scheme: dark)')

watchEffect(() => applyBrowserTheme(preferences.theme, systemDark.value))
</script>

<template>
  <RouterView />
</template>
