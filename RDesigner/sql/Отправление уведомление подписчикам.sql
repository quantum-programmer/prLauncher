-- Создаем функцию для отправки уведомлений
CREATE OR REPLACE FUNCTION notify_report_change()
RETURNS TRIGGER AS $$
BEGIN
    -- Отправляем уведомление только с идентификатором записи
    PERFORM pg_notify('report_change', 'Удалена запись из таблицы Rep_REPORTS');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Создаем триггер для вставки, обновления и удаления записей
CREATE TRIGGER report_change_trigger
AFTER INSERT OR UPDATE OR DELETE ON "Rep_REPORTS"
FOR EACH ROW EXECUTE FUNCTION notify_report_change();