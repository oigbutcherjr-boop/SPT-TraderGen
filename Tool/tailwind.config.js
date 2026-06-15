/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        tarkov: {
          bg: '#1a1a1a',
          surface: '#252525',
          border: '#3a3a3a',
          accent: '#c8aa6e',
          'accent-hover': '#d4b97a',
          text: '#e0e0e0',
          'text-dim': '#888888',
          success: '#4caf50',
          error: '#f44336',
          warning: '#ff9800',
        }
      }
    },
  },
  plugins: [],
}
