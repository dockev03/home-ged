<script setup lang="ts">
import { darkTheme, dateFrFR, frFR, NConfigProvider, NGlobalStyle, NMessageProvider, useOsTheme } from 'naive-ui'
import { computed, watchEffect } from 'vue'
import { applyBrowserTheme } from './format/theme'
import { usePreferencesStore } from './stores/preferences'

// Thème choisi sur cet appareil (PWA-03) ; par défaut, celui du système.
const preferences = usePreferencesStore()
const osTheme = useOsTheme()
const dark = computed(() =>
  preferences.theme === 'systeme' ? osTheme.value === 'dark' : preferences.theme === 'sombre',
)
const theme = computed(() => (dark.value ? darkTheme : null))

watchEffect(() => applyBrowserTheme(preferences.theme))
</script>

<template>
  <NConfigProvider :theme="theme" :locale="frFR" :date-locale="dateFrFR">
    <NGlobalStyle />
    <NMessageProvider>
      <RouterView />
    </NMessageProvider>
  </NConfigProvider>
</template>
