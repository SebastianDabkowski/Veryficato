import { createRoot } from 'react-dom/client';
import { App } from './App';
import './styles.css';
// Authentication consumes a one-time bootstrap token: do not duplicate it with StrictMode effects.
createRoot(document.getElementById('root')!).render(<App />);
