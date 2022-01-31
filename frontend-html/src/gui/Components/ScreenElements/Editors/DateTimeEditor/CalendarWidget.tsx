import { observer } from "mobx-react";
import * as React from "react";
import moment from "moment";
import { action, observable } from "mobx";
import S from "gui/Components/ScreenElements/Editors/DateTimeEditor/CalendarWidget.module.scss";
import cx from "classnames";

@observer
export class CalendarWidget extends React.Component<{
  initialDisplayDate?: moment.Moment;
  selectedDay?: moment.Moment;
  onDayClick?(event: any, day: moment.Moment): void;
}> {
  @observable.ref
  displayedMonth = moment(this.props.initialDisplayDate).startOf("month");

  componentDidUpdate(
    prevProps: Readonly<{ initialDisplayDate?: moment.Moment; selectedDay?: moment.Moment; onDayClick?(event: any, day: moment.Moment): void }>,
    prevState: Readonly<{}>,
    snapshot?: any) {
    if (
      this.props.initialDisplayDate !== prevProps.initialDisplayDate &&
      this.props.initialDisplayDate?.isValid()
    ) {
      this.displayedMonth = moment(this.props.initialDisplayDate).startOf("month");
    }
  }

  getDayHeaders() {
    const result: any[] = [];
    for (
      let day = moment(this.displayedMonth).startOf("week"), i = 0;
      i < 7;
      day.add({days: 1}), i++
    ) {
      result.push(
        <div
          key={day.toISOString()}
          className={S.dayHeaderCell}
          title={day.format("dddd")}
        >
          {day.format("dd")[0]}
        </div>
      );
    }
    return result;
  }

  getDates(weekInc: number) {
    const result: any[] = [];
    const today = moment();
    for (
      let day = moment(this.displayedMonth).startOf("week").add({weeks: weekInc}), i = 0;
      i < 7;
      day.add({days: 1}), i++
    ) {
      const isNeighbourMonth = day.month() !== this.displayedMonth.month();
      const isSelectedDay = day.isSame(this.props.selectedDay, "day");
      const isToday = day.isSame(today, "days");
      const dayCopy = moment(day);
      result.push(
        <div
          key={day.toISOString()}
          className={cx(S.cell, {
            [S.neighbourMonthCell]: isNeighbourMonth,
            [S.selectedDay]: isSelectedDay,
            isToday,
          })}
          onClick={(event: any) => this.handleDayClick(event, dayCopy)}
        >
          {day.format("D")}
        </div>
      );
    }
    return result;
  }

  getDateRows() {
    const result: any[] = [];
    for (let i = 0; i < 6; i++) {
      result.push(<div key={i} className={S.row}>{this.getDates(i)}</div>);
    }
    return result;
  }

  @action.bound
  handleMonthDecClick(event: any) {
    this.displayedMonth = moment(this.displayedMonth).subtract({months: 1});
  }

  @action.bound
  handleMonthIncClick(event: any) {
    this.displayedMonth = moment(this.displayedMonth).add({months: 1});
  }

  @action.bound
  handleYearDecClick(event: any) {
    this.displayedMonth = moment(this.displayedMonth).subtract({years: 1});
  }

  @action.bound
  handleYearIncClick(event: any) {
    this.displayedMonth = moment(this.displayedMonth).add({years: 1});
  }

  @action.bound handleDayClick(event: any, day: moment.Moment) {
    this.props.onDayClick && this.props.onDayClick(event, day);
  }

  render() {
    return (
      <div
        className={S.container}
        onMouseDown={(e) => {
          /* Prevent mousedown default action causong onblur being fired on the editors input
              which triggered data update
          */
          e.preventDefault();
        }}
      >
        <div className={S.dayTable}>
          <div className={S.row}>
            <div className={S.header}>
              <button className={S.controlBtn} onClick={this.handleMonthDecClick}>
                <i className="fas fa-caret-left"/>
              </button>
              <button className={S.controlBtn} onClick={this.handleMonthIncClick}>
                <i className="fas fa-caret-right"/>
              </button>
              <div
                className={S.title}
                title={this.displayedMonth.format("YYYY MMMM")}
              >
                {this.displayedMonth.format("YYYY MMMM")}
              </div>
              <button className={S.controlBtn} onClick={this.handleYearDecClick}>
                <i className="fas fa-caret-down"/>
              </button>
              <button className={S.controlBtn} onClick={this.handleYearIncClick}>
                <i className="fas fa-caret-up"/>
              </button>
            </div>
          </div>
          <div className={S.row}>{this.getDayHeaders()}</div>
          {this.getDateRows()}
        </div>
      </div>
    );
  }
}