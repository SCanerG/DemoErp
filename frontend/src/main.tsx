import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { MutationCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from './App';
import { AuthProvider } from './Auth';
import { ApiError } from './api';
import './styles.css';

const queryClient = new QueryClient({ mutationCache: new MutationCache({ onSuccess: (_data, _variables, _context, mutation) => {
  if (mutation.meta?.affectsBusinessData === false) return;
  void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  void queryClient.invalidateQueries({ queryKey: ['reports'] });
} }), defaultOptions: { queries: { staleTime: 30_000,
  retry: (attempt, error) => !(error instanceof ApiError && error.status < 500) && attempt < 1 } } });

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode><QueryClientProvider client={queryClient}><BrowserRouter><AuthProvider><App /></AuthProvider></BrowserRouter></QueryClientProvider></React.StrictMode>,
);
