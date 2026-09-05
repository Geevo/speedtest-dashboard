import { useEffect, useState } from 'react'

export type ThemePreference = 'system' | 'light' | 'dark'

const storageKey = 'speedtest-dashboard-theme'
const darkScheme = '(prefers-color-scheme: dark)'

function storedPreference(): ThemePreference {
  try {
    const value = window.localStorage.getItem(storageKey)
    return value === 'light' || value === 'dark' ? value : 'system'
  } catch {
    return 'system'
  }
}

export function useTheme() {
  const [theme, setTheme] = useState<ThemePreference>(storedPreference)

  useEffect(() => {
    const root = document.documentElement
    const media = window.matchMedia(darkScheme)
    const themeColor = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')

    const apply = () => {
      if (theme === 'system') {
        delete root.dataset.theme
      } else {
        root.dataset.theme = theme
      }

      const dark = theme === 'dark' || (theme === 'system' && media.matches)
      themeColor?.setAttribute('content', dark ? '#17181a' : '#f6f6f7')
    }

    try {
      if (theme === 'system') window.localStorage.removeItem(storageKey)
      else window.localStorage.setItem(storageKey, theme)
    } catch {
      // The selected theme still applies for this session when storage is unavailable.
    }

    apply()
    media.addEventListener('change', apply)
    return () => media.removeEventListener('change', apply)
  }, [theme])

  return { theme, setTheme }
}
