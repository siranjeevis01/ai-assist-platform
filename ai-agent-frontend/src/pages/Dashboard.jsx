// src/pages/Dashboard.jsx
import React, { useState, useEffect } from 'react';
import { Calendar, CheckSquare, Mail, MessageCircle, TrendingUp } from 'lucide-react';
import { userService, taskService, eventService } from '../services/api';

export default function Dashboard() {
  const [stats, setStats] = useState({ 
    tasks: { total: 0, completed: 0, thisWeek: 0, thisMonth: 0 },
    events: { total: 0, upcoming: 0, thisWeek: 0, thisMonth: 0 }
  });
  const [recentTasks, setRecentTasks] = useState([]);
  const [todayEvents, setTodayEvents] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadDashboardData();
  }, []);

  const loadDashboardData = async () => {
    try {
      const [statsData, tasksData, eventsData] = await Promise.all([
        userService.getStats(),
        taskService.getAll(),
        eventService.getAll()
      ]);

      setStats(statsData);
      setRecentTasks(tasksData.slice(0, 5));
      
      const today = new Date().toDateString();
      const todayEventsData = eventsData.filter(event => 
        event.startUtc && new Date(event.startUtc).toDateString() === today
      ).slice(0, 5);
      setTodayEvents(todayEventsData);
    } catch (error) {
      console.error('Failed to load dashboard data:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleTaskComplete = async (taskId) => {
    try {
      await taskService.complete(taskId);
      loadDashboardData();
    } catch (error) {
      console.error('Failed to complete task:', error);
    }
  };

  if (loading) {
    return <div className="loading">Loading dashboard...</div>;
  }

  return (
    <div className="dashboard">
      <div className="dashboard-header">
        <h1>Dashboard</h1>
        <p>Welcome back! Here's your productivity overview.</p>
      </div>

      {/* Stats Cards */}
      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-icon" style={{background: 'var(--primary-gradient)'}}>
            <CheckSquare size={24} />
          </div>
          <div className="stat-content">
            <h3>{stats.tasks.total || 0}</h3>
            <p>Total Tasks</p>
            <div className="stat-details">
              <span className="completed">{stats.tasks.completed || 0} done</span>
              <span className="in-progress">{stats.tasks.thisWeek || 0} this week</span>
            </div>
          </div>
        </div>

        <div className="stat-card">
          <div className="stat-icon" style={{background: 'var(--secondary-gradient)'}}>
            <Calendar size={24} />
          </div>
          <div className="stat-content">
            <h3>{stats.events.total || 0}</h3>
            <p>Total Events</p>
            <div className="stat-details">
              <span className="upcoming">{stats.events.upcoming || 0} upcoming</span>
              <span className="this-week">{stats.events.thisWeek || 0} this week</span>
            </div>
          </div>
        </div>

        <div className="stat-card">
          <div className="stat-icon" style={{background: 'var(--accent-gradient)'}}>
            <Mail size={24} />
          </div>
          <div className="stat-content">
            <h3>✓</h3>
            <p>Gmail Connected</p>
            <div className="stat-details">
              <span className="synced">Auto-syncing</span>
            </div>
          </div>
        </div>

        <div className="stat-card">
          <div className="stat-icon" style={{background: 'var(--success-gradient)'}}>
            <TrendingUp size={24} />
          </div>
          <div className="stat-content">
            <h3>{stats.tasks.total > 0 ? Math.round((stats.tasks.completed / stats.tasks.total) * 100) : 0}%</h3>
            <p>Completion Rate</p>
            <div className="stat-details">
              <span className="positive">Keep going!</span>
            </div>
          </div>
        </div>
      </div>

      {/* Recent Tasks & Events */}
      <div className="dashboard-content">
        <div className="content-column">
          <div className="content-card">
            <h3>Recent Tasks</h3>
            <div className="task-list">
              {recentTasks.length === 0 ? (
                <p className="empty-state">No tasks found</p>
              ) : (
                recentTasks.map(task => (
                  <div key={task.id} className="task-item">
                    <div className="task-checkbox">
                      <input 
                        type="checkbox" 
                        checked={task.status === 'Done'} 
                        onChange={() => handleTaskComplete(task.id)}
                      />
                    </div>
                    <div className="task-content">
                      <div className={`task-title ${task.status === 'Done' ? 'completed' : ''}`}>
                        {task.title}
                      </div>
                      <div className="task-meta">
                        {task.dueUtc && <span className="due-date">Due: {new Date(task.dueUtc).toLocaleDateString()}</span>}
                        <span className={`status-badge ${task.status?.toLowerCase().replace(' ', '-')}`}>
                          {task.status}
                        </span>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>

        <div className="content-column">
          <div className="content-card">
            <h3>Today's Events</h3>
            <div className="event-list">
              {todayEvents.length === 0 ? (
                <p className="empty-state">No events today</p>
              ) : (
                todayEvents.map(event => (
                  <div key={event.id} className="event-item">
                    <div className="event-time">
                      {event.startUtc ? new Date(event.startUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : 'All day'}
                    </div>
                    <div className="event-content">
                      <div className="event-title">{event.title}</div>
                      {event.location && <div className="event-location">{event.location}</div>}
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Quick Actions */}
      <div className="quick-actions">
        <h3>Quick Actions</h3>
        <div className="action-buttons">
          <button className="action-btn" onClick={() => window.location.href = '/tasks'}>
            <CheckSquare size={20} /> Add Task
          </button>
          <button className="action-btn" onClick={() => window.location.href = '/calendar'}>
            <Calendar size={20} /> Schedule Event
          </button>
          <button className="action-btn" onClick={() => window.location.href = '/messages'}>
            <MessageCircle size={20} /> Chat with AI
          </button>
        </div>
      </div>
    </div>
  );
}