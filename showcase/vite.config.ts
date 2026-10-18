import { defineConfig } from 'vite';
import { resolve } from 'node:path';
export default defineConfig({envDir:false,envPrefix:'SHOWCASE_PUBLIC_UNUSED_',build:{modulePreload:{polyfill:false},rollupOptions:{input:{main:resolve(import.meta.dirname,'index.html'),tr:resolve(import.meta.dirname,'tr/index.html'),en:resolve(import.meta.dirname,'en/index.html')}}}});