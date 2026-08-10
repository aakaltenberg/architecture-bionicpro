import React, { useState, useEffect } from 'react';

const AUTH_URL = process.env.REACT_APP_AUTH_URL || 'http://localhost:5000';
const API_BASE = process.env.REACT_APP_API_BASE_PATH || '/api';

const ReportPage: React.FC = () => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [report, setReport] = useState<string | null>(null);
  const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null); // null = загрузка

  // Проверяем статус аутентификации при монтировании и при изменении куки (например, после login/logout)
  useEffect(() => {
    const checkAuth = async () => {
      try {
        const res = await fetch(`${AUTH_URL}/auth/me`, {
          credentials: 'include',
        });
        setIsAuthenticated(res.ok);
      } catch {
        setIsAuthenticated(false);
      }
    };
    checkAuth();
  }, []);

  const downloadReport = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await fetch(`${AUTH_URL}/api/reports`, {
        credentials: 'include',
      });
      if (!response.ok) throw new Error('Failed to get report');
      const data = await response.json();
      window.location.href = data.url; 
    } catch (err: any) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  // Пока проверка не завершилась, показываем индикатор загрузки
  if (isAuthenticated === null) {
    return <div className="flex items-center justify-center min-h-screen">Loading...</div>;
  }

  // Неавторизованный пользователь видит только кнопку Login
  if (!isAuthenticated) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
        <div className="p-8 bg-white rounded-lg shadow-md text-center">
          <h1 className="text-xl font-bold mb-4">BionicPRO Reports</h1>
          <p className="mb-6 text-gray-600">Please log in to access your usage reports.</p>
          <a
            href={`${AUTH_URL}/auth/login`}
            className="px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 inline-block"
          >
            Login
          </a>
        </div>
      </div>
    );
  }

  // Авторизованный пользователь видит интерфейс отчётов и Logout
  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-gray-100">
      <div className="p-8 bg-white rounded-lg shadow-md">
        <h1 className="text-2xl font-bold mb-6">Usage Reports</h1>
        <button
          onClick={downloadReport}
          disabled={loading}
          className={`px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 ${loading ? 'opacity-50 cursor-not-allowed' : ''
            }`}
        >
          {loading ? 'Generating Report...' : 'Download Report'}
        </button>
        {report && (
          <pre className="mt-4 p-4 bg-gray-50 rounded overflow-auto max-h-96">
            {report}
          </pre>
        )}
        {error && (
          <div className="mt-4 p-4 bg-red-100 text-red-700 rounded">{error}</div>
        )}
      </div>
      <div className="mt-4">
        <a href={`${AUTH_URL}/auth/logout`} className="text-red-500 underline">
          Logout
        </a>
      </div>
    </div>
  );
};

export default ReportPage;