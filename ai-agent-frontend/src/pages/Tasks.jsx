// src/pages/Tasks.jsx
import React, { useState, useEffect } from 'react';
import { Plus, Search, Filter, MoreVertical, Calendar, Flag } from 'lucide-react';
import { taskService } from '../services/api';

export default function Tasks() {
  const [tasks, setTasks] = useState([]);
  const [filter, setFilter] = useState('all');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [showNewTask, setShowNewTask] = useState(false);
  const [newTask, setNewTask] = useState({
    title: '',
    description: '',
    dueUtc: '',
    status: 'To Do',
    priority: 'Medium'
  });

  useEffect(() => {
    loadTasks();
  }, [filter]);

  const loadTasks = async () => {
    try {
      const tasksData = await taskService.getAll(filter === 'all' ? '' : filter);
      setTasks(tasksData);
    } catch (error) {
      console.error('Failed to load tasks:', error);
    } finally {
      setLoading(false);
    }
  };

  const createTask = async (e) => {
    e.preventDefault();
    try {
      await taskService.create(newTask);
      setShowNewTask(false);
      setNewTask({ 
        title: '', 
        description: '', 
        dueUtc: '', 
        status: 'To Do',
        priority: 'Medium'
      });
      loadTasks();
    } catch (error) {
      console.error('Failed to create task:', error);
      alert('Failed to create task. Please try again.');
    }
  };

  const updateTaskStatus = async (taskId, newStatus) => {
    try {
      await taskService.update(taskId, { status: newStatus });
      loadTasks();
    } catch (error) {
      console.error('Failed to update task:', error);
      alert('Failed to update task status.');
    }
  };

  const deleteTask = async (taskId) => {
    if (window.confirm('Are you sure you want to delete this task?')) {
      try {
        await taskService.delete(taskId);
        loadTasks();
      } catch (error) {
        console.error('Failed to delete task:', error);
        alert('Failed to delete task.');
      }
    }
  };

  const getPriorityColor = (priority) => {
    switch (priority?.toLowerCase()) {
      case 'high': return '#f5576c';
      case 'medium': return '#ffa726';
      case 'low': return '#43e97b';
      default: return '#667eea';
    }
  };

  const filteredTasks = tasks.filter(task =>
    task.title.toLowerCase().includes(search.toLowerCase()) ||
    task.description?.toLowerCase().includes(search.toLowerCase())
  );

  const getTaskCounts = () => {
    const total = tasks.length;
    const completed = tasks.filter(t => t.status === 'Done').length;
    const inProgress = tasks.filter(t => t.status === 'In Progress').length;
    const todo = tasks.filter(t => t.status === 'To Do').length;
    
    return { total, completed, inProgress, todo };
  };

  const counts = getTaskCounts();

  return (
    <div className="tasks-page">
      <div className="page-header">
        <div className="header-content">
          <h1>Tasks</h1>
          <p>Manage your tasks and to-dos</p>
        </div>
        <button 
          className="btn btn-primary"
          onClick={() => setShowNewTask(true)}
        >
          <Plus size={20} />
          New Task
        </button>
      </div>

      {/* Task Statistics */}
      <div className="task-stats">
        <div className="stat-item">
          <div className="stat-number">{counts.total}</div>
          <div className="stat-label">Total</div>
        </div>
        <div className="stat-item">
          <div className="stat-number" style={{color: '#43e97b'}}>{counts.completed}</div>
          <div className="stat-label">Done</div>
        </div>
        <div className="stat-item">
          <div className="stat-number" style={{color: '#ffa726'}}>{counts.inProgress}</div>
          <div className="stat-label">In Progress</div>
        </div>
        <div className="stat-item">
          <div className="stat-number" style={{color: '#667eea'}}>{counts.todo}</div>
          <div className="stat-label">To Do</div>
        </div>
      </div>

      {/* Filters and Search */}
      <div className="tasks-controls">
        <div className="search-box">
          <Search size={20} />
          <input
            type="text"
            placeholder="Search tasks..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="filter-buttons">
          {['all', 'To Do', 'In Progress', 'Done'].map(status => (
            <button
              key={status}
              className={`filter-btn ${filter === status ? 'active' : ''}`}
              onClick={() => setFilter(status)}
            >
              {status}
            </button>
          ))}
        </div>
      </div>

      {/* New Task Modal */}
      {showNewTask && (
        <div className="modal-overlay">
          <div className="modal">
            <h2>Create New Task</h2>
            <form onSubmit={createTask}>
              <div className="form-group">
                <label>Title *</label>
                <input
                  type="text"
                  value={newTask.title}
                  onChange={(e) => setNewTask({ ...newTask, title: e.target.value })}
                  required
                  placeholder="What needs to be done?"
                />
              </div>
              <div className="form-group">
                <label>Description</label>
                <textarea
                  value={newTask.description}
                  onChange={(e) => setNewTask({ ...newTask, description: e.target.value })}
                  rows="3"
                  placeholder="Add details about the task..."
                />
              </div>
              <div className="form-row">
                <div className="form-group">
                  <label>Due Date</label>
                  <input
                    type="datetime-local"
                    value={newTask.dueUtc}
                    onChange={(e) => setNewTask({ ...newTask, dueUtc: e.target.value })}
                  />
                </div>
                <div className="form-group">
                  <label>Priority</label>
                  <select
                    value={newTask.priority}
                    onChange={(e) => setNewTask({ ...newTask, priority: e.target.value })}
                  >
                    <option value="Low">Low</option>
                    <option value="Medium">Medium</option>
                    <option value="High">High</option>
                  </select>
                </div>
              </div>
              <div className="form-group">
                <label>Status</label>
                <select
                  value={newTask.status}
                  onChange={(e) => setNewTask({ ...newTask, status: e.target.value })}
                >
                  <option value="To Do">To Do</option>
                  <option value="In Progress">In Progress</option>
                  <option value="Done">Done</option>
                </select>
              </div>
              <div className="modal-actions">
                <button 
                  type="button" 
                  className="btn btn-secondary"
                  onClick={() => setShowNewTask(false)}
                >
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary">
                  Create Task
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Tasks List */}
      <div className="tasks-list">
        {loading ? (
          <div className="loading">Loading tasks...</div>
        ) : filteredTasks.length === 0 ? (
          <div className="empty-state">
            <p>{search ? 'No tasks match your search' : 'No tasks found'}</p>
            {!search && (
              <button 
                className="btn btn-primary"
                onClick={() => setShowNewTask(true)}
              >
                Create Your First Task
              </button>
            )}
          </div>
        ) : (
          filteredTasks.map(task => (
            <div key={task.id} className="task-card">
              <div className="task-main">
                <div className="task-checkbox">
                  <input
                    type="checkbox"
                    checked={task.status === 'Done'}
                    onChange={(e) => updateTaskStatus(
                      task.id, 
                      e.target.checked ? 'Done' : 'To Do'
                    )}
                  />
                </div>
                <div className="task-content">
                  <h4 className={task.status === 'Done' ? 'completed' : ''}>
                    {task.title}
                  </h4>
                  {task.description && (
                    <p className="task-description">{task.description}</p>
                  )}
                  <div className="task-meta">
                    {task.dueUtc && (
                      <span className="due-date">
                        <Calendar size={14} />
                        Due: {new Date(task.dueUtc).toLocaleDateString()}
                      </span>
                    )}
                    <span 
                      className="priority-badge"
                      style={{ backgroundColor: getPriorityColor(task.priority) }}
                    >
                      <Flag size={12} />
                      {task.priority || 'Medium'}
                    </span>
                    <span className={`status-badge ${task.status.toLowerCase().replace(' ', '-')}`}>
                      {task.status}
                    </span>
                  </div>
                </div>
              </div>
              <div className="task-actions">
                <button 
                  className="icon-btn"
                  onClick={() => deleteTask(task.id)}
                >
                  <MoreVertical size={16} />
                </button>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}