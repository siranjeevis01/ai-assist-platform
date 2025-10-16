// src/context/AuthContext.jsx
import React, { createContext, useContext, useState, useEffect } from 'react';
import { authService } from '../services/api';

const AuthContext = createContext();

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    const token = localStorage.getItem('token');
    if (token) {
      authService.getCurrentUser()
        .then(userData => {
          setUser(userData);
          setError('');
        })
        .catch((error) => {
          console.error('Auth error:', error);
          localStorage.removeItem('token');
          setError('Session expired. Please login again.');
        })
        .finally(() => {
          setLoading(false);
        });
    } else {
      setLoading(false);
    }
  }, []);

  const login = async (email, password) => {
    try {
      setLoading(true);
      setError('');
      const response = await authService.login(email, password);
      const { token, user: userData } = response;
      
      localStorage.setItem('token', token);
      setUser(userData);
      
      return { success: true, user: userData };
    } catch (error) {
      const errorMessage = error.response?.data?.message || error.message || 'Login failed';
      setError(errorMessage);
      return { success: false, error: errorMessage };
    } finally {
      setLoading(false);
    }
  };

  const register = async (name, email, password) => {
    try {
      setLoading(true);
      setError('');
      const response = await authService.register({ name, email, password });
      const { token, user: userData } = response;
      
      localStorage.setItem('token', token);
      setUser(userData);
      
      return { success: true, user: userData };
    } catch (error) {
      const errorMessage = error.response?.data?.message || error.message || 'Registration failed';
      setError(errorMessage);
      return { success: false, error: errorMessage };
    } finally {
      setLoading(false);
    }
  };

  const logout = () => {
    localStorage.removeItem('token');
    setUser(null);
    setError('');
  };

  const updateUser = (updates) => {
    setUser(prev => ({ ...prev, ...updates }));
  };

  const clearError = () => {
    setError('');
  };

  const value = {
    user,
    login,
    register,
    logout,
    loading,
    error,
    updateUser,
    clearError,
    isAuthenticated: !!user
  };

  return (
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
}