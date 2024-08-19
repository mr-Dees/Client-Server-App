using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ChatClient
{
	class Program
	{
		// Событие дли синхронизации завершения потоков отправки и принятия данных
		static ManualResetEvent exitEvent = new ManualResetEvent(false);

		// Флаг для правильного отображения приглашений при выходе
		static bool isExit = false;

		static void Main(string[] args)
		{
			// Установка русской локали
			Console.OutputEncoding = Encoding.UTF8;

			// Создание клиентского сокета
			Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			// Настройка адреса сервера
			IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse("172.20.10.4"), 8080);

			try
			{
				// Подключение к серверу
				clientSocket.Connect(serverEndPoint);

				// Ввод логина клиента
				Console.Write("Введите логин: ");
				string login = Console.ReadLine();

				// Отправка логина серверу
				byte[] loginBytes = Encoding.UTF8.GetBytes(login);
				clientSocket.Send(loginBytes);

				// Получение ответа от сервера о результате проверки логина
				byte[] loginResponseBytes = new byte[1024];
				int loginResponseBytesRead = clientSocket.Receive(loginResponseBytes);
				string loginResponse = Encoding.UTF8.GetString(loginResponseBytes, 0, loginResponseBytesRead);

				// Если логин свободен, то начинаем работу на сервере
				if (loginResponse == "OK")
				{
					Console.WriteLine("Подключено к серверу");

					// Создание потока для получения сообщений от сервера
					Thread receiveThread = new Thread(ReceiveMessages);
					receiveThread.Start(clientSocket);

					while (true)
					{
						// Перемещение курсора в начало текущей строки
						Console.SetCursorPosition(0, Console.CursorTop);
						// Очистка текущей строки
						Console.Write(new string(' ', Console.WindowWidth));
						// Перемещение курсора в начало текущей строки
						Console.SetCursorPosition(0, Console.CursorTop);
						// Ввод команды с клавиатуры
						Console.Write("Введите команду: ");
						string command = Console.ReadLine();

						// Отправка команды серверу
						byte[] commandBytes = Encoding.UTF8.GetBytes(command);
						clientSocket.Send(commandBytes);

						// Проверка на команду bye для завершения работы
						if (command.ToLower() == "bye")
						{
							isExit = true; // Устанавливаем флаг, что мы завершаем соединение
							exitEvent.Set(); // Сигнализируем поток о необходимости завершения
							break;
						}
					}
					receiveThread.Join(); // Ожидаем завершения потока
				}
				// Если логин занят, то завершаем сеанс
				else if (loginResponse == "TAKEN")
				{
					Console.WriteLine("Логин уже занят. Подключение невозможно.");
				}
			}
			// Если при работе с сетью возникла ошибка, перехватываем и выводим ее
			catch (Exception ex)
			{
				Console.WriteLine("Ошибка: " + ex.Message);
			}
			// Гарантируем закрытие сокета при завершении работы
			finally
			{
				// Закрытие клиентского сокета
				clientSocket.Close();
			}
		}

		static void ReceiveMessages(object socket)
		{
			// Приводим переменную типа объект к типу сокет, для коректной работы
			Socket clientSocket = (Socket)socket;

			try
			{
				// Проверяем событие выхода перед каждой итерацией цикла
				while (!exitEvent.WaitOne(0))
				{
					// Получение данных от сервера
					byte[] buffer = new byte[1024];
					int bytesRead = clientSocket.Receive(buffer);
					string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

					// Перемещение курсора в начало текущей строки
					Console.SetCursorPosition(0, Console.CursorTop);
					// Очистка текущей строки
					Console.Write(new string(' ', Console.WindowWidth));
					// Перемещение курсора в начало текущей строки
					Console.SetCursorPosition(0, Console.CursorTop);
					// Вывод полученного сообщения
					Console.WriteLine(message);
					// Если нет разрыва вывод приглашения к вводу
					if (!isExit) 
						Console.Write("Введите команду: ");
				}
			}
			// Если при приеме сообщений возникла ошибка, перехватываем и выводим ее
			catch (Exception ex)
			{
				Console.WriteLine("Ошибка: " + ex.Message);
			}
		}
	}
}
