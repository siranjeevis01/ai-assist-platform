// src/services/api.js
import axios from 'axios';

const API_BASE_URL = 'http://localhost:5000';

const api = axios.create({
  baseURL: API_BASE_URL,
  timeout: 10000,
  headers: {
    'Content-Type': 'application/json',
  }
});

// Add token to requests
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Handle responses and errors
api.interceptors.response.use(
  (response) => {
    return response;
  },
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token');
      window.location.href = '/login';
    }
    
    // Enhanced error handling
    if (error.code === 'ECONNABORTED') {
      console.error('Request timeout:', error.config.url);
      throw new Error('Request timeout. Please try again.');
    }
    
    if (!error.response) {
      console.error('Network error:', error);
      throw new Error('Network error. Please check your connection.');
    }
    
    return Promise.reject(error);
  }
);

export const authService = {
  login: (email, password) => 
    api.post('/api/Auth/login', { email, password }).then(res => res.data),
  
  register: (userData) =>
    api.post('/api/Auth/register', userData).then(res => res.data),
  
  getCurrentUser: () => 
    api.get('/api/Users/me').then(res => res.data),

  refreshToken: () =>
    api.post('/api/Auth/refresh').then(res => res.data),

  logout: () =>
    api.post('/api/Auth/logout').then(res => res.data),

  forgotPassword: (email) =>
    api.post('/api/Auth/forgot-password', { email }).then(res => res.data),

  resetPassword: (token, newPassword) =>
    api.post('/api/Auth/reset-password', { token, newPassword }).then(res => res.data),
};

export const taskService = {
  getAll: (status = '') => 
    api.get('/api/Tasks', { 
      params: { status },
      timeout: 5000 
    }).then(res => res.data),
  
  create: (task) => 
    api.post('/api/Tasks', task).then(res => res.data),
  
  update: (id, updates) => 
    api.patch(`/api/Tasks/${id}`, updates).then(res => res.data),
  
  complete: (id) => 
    api.post(`/api/Tasks/${id}/complete`).then(res => res.data),
  
  delete: (id) => 
    api.delete(`/api/Tasks/${id}`).then(res => res.data),
};

export const eventService = {
  getAll: (from, to) => 
    api.get('/api/Events', { 
      params: { 
        from: from || new Date().toISOString(),
        to: to || new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString()
      },
      timeout: 5000
    }).then(res => res.data),
  
  create: (event) => 
    api.post('/api/Events', event).then(res => res.data),
  
  update: (id, updates) => 
    api.patch(`/api/Events/${id}`, updates).then(res => res.data),
  
  delete: (id) => 
    api.delete(`/api/Events/${id}`).then(res => res.data),
};

export const messageService = {
  send: (text) => 
    api.post('/api/Messages/send', { text }).then(res => res.data),
  
  getHistory: () => 
    api.get('/api/Messages/history').then(res => res.data),
  
  getConversations: () =>
    api.get('/api/Messages/conversations').then(res => res.data),
};

export const integrationService = {
  // Google Services
  getGoogleStatus: () => 
    api.get('/api/Google/status').then(res => res.data),
  
  connectGoogle: () => 
    api.get('/api/Google/connect').then(res => res.data),
  
  disconnectGoogle: () => 
    api.delete('/api/Google/disconnect').then(res => res.data),
  
  syncGoogleCalendar: () =>
    api.post('/api/Google/calendar/sync').then(res => res.data),

  // WhatsApp Services
  getWhatsAppStatus: () => 
    api.get('/api/WhatsApp/status').then(res => res.data),
  
  connectWhatsApp: () => 
    api.post('/api/WhatsApp/connect').then(res => res.data),
  
  disconnectWhatsApp: () =>
    api.delete('/api/WhatsApp/disconnect').then(res => res.data),
  
  getQRCode: () => 
    api.get('/api/WhatsApp/qr').then(res => res.data),
  
  testWhatsApp: () => 
    api.post('/api/WhatsApp/test').then(res => res.data),

  // Trello Services
  getTrelloStatus: () =>
    api.get('/api/Trello/status').then(res => res.data),

  connectTrello: () =>
    api.get('/api/Trello/connect').then(res => res.data),

  // Gmail Services
  getGmailStatus: () =>
    api.get('/api/Gmail/status').then(res => res.data),

  syncGmail: () =>
    api.post('/api/Gmail/sync').then(res => res.data),
};

export const userService = {
  getStats: () => 
    api.get('/api/Users/stats').then(res => res.data),
  
  updateProfile: (updates) => 
    api.put('/api/Users/me', updates).then(res => res.data),

  getPreferences: () =>
    api.get('/api/Users/preferences').then(res => res.data),

  updatePreferences: (preferences) =>
    api.put('/api/Users/preferences', preferences).then(res => res.data),
};

// Utility function to check API health
export const checkAPIHealth = async () => {
  try {
    const response = await api.get('/health');
    return response.status === 200;
  } catch (error) {
    console.error('API health check failed:', error);
    return false;
  }
};

export default api;